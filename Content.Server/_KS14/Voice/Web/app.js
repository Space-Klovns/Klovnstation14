"use strict";

// Voice chat microphone page. See Klovn/Docs/voice_chat.md for the protocol and the security model.

(() => {
    const PROTOCOL_VERSION = 1;
    const SAMPLE_RATE = 16000;
    const FRAME_SAMPLES = 320;          // 20 ms at 16 kHz
    const FRAMES_PER_MESSAGE = 3;       // 60 ms per websocket message
    const PREROLL_FRAMES = 2;           // sent when the gate opens, so word onsets aren't clipped
    const HANGOVER_FRAMES = 15;         // 300 ms of gate hold after the level drops
    const PING_INTERVAL_MS = 20000;
    const MAX_RECONNECTS = 5;
    const TOKEN_KEY = "ksVoiceToken";
    const GATE_KEY = "ksVoiceGateDb";
    const DEVICE_KEY = "ksVoiceDevice";
    const VOLUME_KEY = "ksVoiceMicVolume";
    const MONITOR_LEAD_SECONDS = 0.05;  // mic test: how far ahead of now to schedule, to ride out message jitter
    const MONITOR_MAX_AHEAD_SECONDS = 0.3; // ...and how far ahead it may run before frames are dropped instead

    // Close reasons after which reconnecting cannot help.
    const TERMINAL_REASONS = new Set(["auth-failed", "auth-timeout", "link-reset", "replaced", "disabled"]);

    const REASON_TEXT = {
        "not-holding-key": "Connected. Hold your push-to-talk key in-game to talk.",
        "admin-muted": "An admin has muted your voice chat.",
        "auto-muted": "Your voice chat is muted automatically because your audio was far too loud.",
        "cooldown": "You've been talking for too long without a break. Wait a moment.",
        "cannot-speak": "Your character can't speak right now.",
        "no-body": "You need to be in the round, in a body, to talk.",
        "disabled": "Voice chat is disabled on this server.",
    };

    const CLOSE_TEXT = {
        "auth-failed": "This voice link is invalid or expired. Get a fresh link in-game.",
        "auth-timeout": "The server didn't receive the voice link in time. Reload the page.",
        "link-reset": "Your voice link was reset. Open the new link from the game.",
        "replaced": "Your voice chat was opened in another tab or browser.",
        "disabled": "Voice chat was disabled by the server.",
        "rate": "Too much audio was sent too quickly; reload the page.",
        "bad-audio": "The server rejected the audio format; reload the page.",
    };

    const $ = (id) => document.getElementById(id);

    const elements = {
        identity: $("identity"),
        insecure: $("insecure"),
        noToken: $("no-token"),
        main: $("main"),
        status: $("status"),
        statusText: $("status-text"),
        start: $("start"),
        stop: $("stop"),
        device: $("device"),
        meterFill: $("meter-fill"),
        meterGate: $("meter-gate"),
        gate: $("gate"),
        gateValue: $("gate-value"),
        volume: $("volume"),
        volumeValue: $("volume-value"),
        test: $("test"),
        testHint: $("test-hint"),
        talkHint: $("talk-hint"),
    };

    const storage = {
        get(area, key) {
            try {
                return window[area].getItem(key);
            } catch {
                return null;
            }
        },
        set(area, key, value) {
            try {
                window[area].setItem(key, value);
            } catch {
                // Storage unavailable; settings just won't persist.
            }
        },
    };

    // The token arrives in the URL fragment, which browsers never send to any server. Move it out of the address
    // bar immediately (so it doesn't end up in screenshots or streams), keeping it in this tab's session storage so
    // a reload still works.
    function takeToken() {
        const fragment = window.location.hash.startsWith("#") ? window.location.hash.slice(1) : "";
        if (fragment) {
            storage.set("sessionStorage", TOKEN_KEY, fragment);
            history.replaceState(null, "", window.location.pathname + window.location.search);
            return fragment;
        }

        return storage.get("sessionStorage", TOKEN_KEY);
    }

    const token = takeToken();

    let socket = null;
    let socketClosedByUs = false;
    let reconnects = 0;
    let pingTimer = null;
    let terminal = false;

    let audioContext = null;
    let mediaStream = null;
    let captureNode = null;
    let inputGain = null;

    // What the status line shows is derived from these, in render(), rather than set piecemeal: the server's view
    //      (push-to-talk held and allowed) says nothing about whether this page is actually capturing anything.
    let micState = "off";               // "off" | "starting" | "on" | "needs-gesture"
    let micGeneration = 0;              // bumped by every start and stop, so a superseded start can tell
    let micError = null;                // why the last start failed, shown until the next attempt
    let linkState = "connecting";       // "connecting" | "reconnecting" | "connected" | "failed"
    let linkError = null;               // why the link failed for good
    let serverState = null;             // last "state" message from the server

    let sequence = 0;
    let gateOpenFrames = 0;
    let pending = [];
    let preroll = [];
    let gateDb = Number(storage.get("localStorage", GATE_KEY) ?? -50);
    let volumePercent = Number(storage.get("localStorage", VOLUME_KEY) ?? 100);

    // Mic test: plays back what passes the noise gate, which is exactly what gets sent.
    let testing = false;
    let monitorTime = 0;                // audio-clock time the next played-back frame starts at

    function setStatus(state, text) {
        elements.status.dataset.state = state;
        elements.statusText.textContent = text;
    }

    function render() {
        elements.test.disabled = micState !== "on";
        elements.test.textContent = testing ? "Stop test" : "Test microphone";
        elements.test.setAttribute("aria-pressed", String(testing));
        elements.testHint.hidden = !testing;
        elements.talkHint.textContent = serverState?.voiceActivation
            ? "Voice activation is on: you're heard whenever your level is above the noise gate, so set the gate just above your background noise."
            : "Hold your in-game push-to-talk key to talk.";

        if (linkState === "failed") {
            setStatus("error", linkError);
            return;
        }

        if (micState === "starting") {
            setStatus("waiting", "Starting the microphone… If your browser asks, allow microphone access.");
            return;
        }

        if (micState === "needs-gesture") {
            setStatus("waiting", "Click anywhere on this page to finish starting the microphone.");
            return;
        }

        if (micState === "off") {
            if (micError)
                setStatus("error", micError);
            else if (serverState?.transmitting && !serverState.voiceActivation)
                setStatus("blocked", "Your microphone is off, so nothing is being sent, even while you hold push-to-talk. Click “Start microphone”.");
            else
                setStatus("idle", "Your microphone is off. Click “Start microphone” to be able to talk.");

            return;
        }

        if (linkState !== "connected") {
            setStatus("waiting", linkState === "reconnecting" ? "Reconnecting…" : "Connecting…");
            return;
        }

        if (volumePercent === 0) {
            setStatus("blocked", "Your mic volume is at 0%, so nothing is being sent. Turn it up to talk.");
            return;
        }

        if (!serverState || serverState.reason === "not-holding-key") {
            setStatus("waiting", REASON_TEXT["not-holding-key"]);
            return;
        }

        if (serverState.transmitting) {
            // With voice activation the server lets everything through, so what's actually going out is up to the gate.
            if (serverState.voiceActivation && gateOpenFrames === 0)
                setStatus("ready", "Voice activation is on. Speak to talk in-game.");
            else
                setStatus("transmitting", "Transmitting in-game.");

            return;
        }

        let text = REASON_TEXT[serverState.reason] ?? "Not transmitting.";
        if (typeof serverState.seconds === "number" && serverState.seconds > 0)
            text += ` (${serverState.seconds}s left)`;

        setStatus("blocked", text);
    }

    function setGate(value) {
        gateDb = value;
        elements.gate.value = String(value);
        elements.gateValue.textContent = `${value} dB`;
        elements.meterGate.style.left = `${dbToMeter(value) * 100}%`;
        storage.set("localStorage", GATE_KEY, String(value));
    }

    function setVolume(value) {
        volumePercent = value;
        elements.volume.value = String(value);
        elements.volumeValue.textContent = `${value}%`;
        inputGain?.gain.setTargetAtTime(value / 100, inputGain.context.currentTime, 0.02);
        storage.set("localStorage", VOLUME_KEY, String(value));
        render();
    }

    function setTesting(value) {
        testing = value && micState === "on";
        monitorTime = 0;
        render();
    }

    // Plays frames back through this page's own output, each scheduled right after the previous one. Called only
    //      once the frames are queued for sending, and never throws: a failing test must not stop the talking.
    function monitorFrames(frames) {
        if (!testing || !audioContext)
            return;

        try {
            for (const frame of frames) {
                const now = audioContext.currentTime;

                // A burst of late frames: drop what doesn't fit rather than let the delay grow, or overlap what's
                //      already scheduled by pulling the schedule back.
                if (monitorTime > now + MONITOR_MAX_AHEAD_SECONDS)
                    continue;

                // The previous frame has finished (the gate was closed): start a new run a little ahead of now.
                if (monitorTime < now)
                    monitorTime = now + MONITOR_LEAD_SECONDS;

                // At the wire's sample rate, so what's heard also has the bandwidth the game gets.
                const buffer = audioContext.createBuffer(1, frame.length, SAMPLE_RATE);
                const samples = buffer.getChannelData(0);
                for (let i = 0; i < frame.length; i++)
                    samples[i] = frame[i] / 32768;

                const player = audioContext.createBufferSource();
                player.buffer = buffer;
                player.connect(audioContext.destination);
                player.start(monitorTime);
                monitorTime += buffer.duration;
            }
        } catch (error) {
            console.error("Mic test playback failed:", error);
            testing = false;
            render();
        }
    }

    function dbToMeter(db) {
        return Math.min(1, Math.max(0, (db + 70) / 70));
    }

    function socketUrl() {
        const url = new URL("ws", window.location.href);
        url.protocol = url.protocol === "https:" ? "wss:" : "ws:";
        return url.toString();
    }

    function connect() {
        if (terminal)
            return;

        socketClosedByUs = false;
        socket = new WebSocket(socketUrl());
        socket.binaryType = "arraybuffer";

        socket.addEventListener("open", () => {
            socket.send(JSON.stringify({ type: "auth", token }));
            pingTimer = setInterval(() => {
                if (socket?.readyState === WebSocket.OPEN)
                    socket.send(JSON.stringify({ type: "ping" }));
            }, PING_INTERVAL_MS);
        });

        socket.addEventListener("message", (event) => {
            if (typeof event.data !== "string")
                return;

            let message;
            try {
                message = JSON.parse(event.data);
            } catch {
                return;
            }

            handleServerMessage(message);
        });

        socket.addEventListener("close", (event) => {
            clearInterval(pingTimer);
            pingTimer = null;
            socket = null;

            if (socketClosedByUs)
                return;

            const reason = event.reason || "";
            serverState = null;
            elements.identity.textContent = "Not connected.";

            if (TERMINAL_REASONS.has(reason)) {
                terminal = true;
                failLink(CLOSE_TEXT[reason] ?? `Disconnected (${reason}).`);
                stopMicrophone();
                return;
            }

            if (reconnects >= MAX_RECONNECTS) {
                failLink(CLOSE_TEXT[reason] ?? "Lost connection to the server. Reload the page to try again.");
                return;
            }

            reconnects++;
            linkState = "reconnecting";
            render();
            setTimeout(connect, 1000 * reconnects);
        });
    }

    function failLink(text) {
        linkState = "failed";
        linkError = text;
        render();
    }

    function handleServerMessage(message) {
        switch (message.type) {
            case "hello":
                reconnects = 0;
                linkState = "connected";
                elements.identity.textContent = `Connected as ${message.name}.`;
                render();
                break;
            case "state":
                serverState = message;
                render();
                break;
            case "closing":
                if (TERMINAL_REASONS.has(message.reason))
                    terminal = true;
                break;
        }
    }

    function sendFrames(frames) {
        if (!socket || socket.readyState !== WebSocket.OPEN || frames.length === 0)
            return;

        const buffer = new ArrayBuffer(4 + frames.length * FRAME_SAMPLES * 2);
        const view = new DataView(buffer);
        view.setUint8(0, PROTOCOL_VERSION);
        view.setUint8(1, 0);
        view.setUint16(2, sequence, true);
        sequence = (sequence + 1) & 0xFFFF;

        let offset = 4;
        for (const frame of frames) {
            for (let i = 0; i < frame.length; i++, offset += 2)
                view.setInt16(offset, frame[i], true);
        }

        socket.send(buffer);
    }

    function onFrame(frame, rms) {
        const db = rms > 0 ? 20 * Math.log10(rms / 32767) : -120;
        elements.meterFill.style.width = `${dbToMeter(db) * 100}%`;

        const wasOpen = gateOpenFrames > 0;
        if (db >= gateDb)
            gateOpenFrames = HANGOVER_FRAMES;
        else if (gateOpenFrames > 0)
            gateOpenFrames--;

        // With voice activation the status follows the gate.
        if (wasOpen !== gateOpenFrames > 0)
            render();

        if (gateOpenFrames === 0) {
            if (pending.length > 0) {
                sendFrames(pending);
                pending = [];
            }

            preroll.push(frame);
            if (preroll.length > PREROLL_FRAMES)
                preroll.shift();

            return;
        }

        const opened = preroll;
        if (preroll.length > 0) {
            pending.push(...preroll);
            preroll = [];
        }

        pending.push(frame);
        while (pending.length >= FRAMES_PER_MESSAGE) {
            sendFrames(pending.slice(0, FRAMES_PER_MESSAGE));
            pending = pending.slice(FRAMES_PER_MESSAGE);
        }

        monitorFrames([...opened, frame]);
    }

    function microphoneErrorText(error) {
        switch (error?.name) {
            case "NotAllowedError":
            case "SecurityError":
                return "Microphone access was blocked. Allow it for this site (the icon at the left of the address bar), then click “Start microphone”.";
            case "NotFoundError":
            case "OverconstrainedError":
                return "No microphone was found. Plug one in, then click “Start microphone”.";
            case "NotReadableError":
                return "Your microphone is in use by another program, or couldn't be opened. Close it there, then click “Start microphone”.";
            default:
                return `Couldn't open the microphone: ${error?.message || error?.name || error}`;
        }
    }

    async function startMicrophone() {
        if (micState !== "off" || terminal)
            return;

        const generation = ++micGeneration;
        micState = "starting";
        micError = null;
        elements.start.disabled = true;
        render();

        // Built up in locals and only published once this start is known to still be wanted: a stop, or a newer
        //      start, can happen while the browser's permission prompt is open.
        let stream = null;
        let context = null;
        try {
            const deviceId = elements.device.value || storage.get("localStorage", DEVICE_KEY) || undefined;
            stream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    deviceId: deviceId ? { ideal: deviceId } : undefined,
                    channelCount: 1,
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true,
                },
            });

            context = new AudioContext();
            await context.audioWorklet.addModule("worklet.js");
        } catch (error) {
            stream?.getTracks().forEach((track) => track.stop());
            context?.close();

            if (generation === micGeneration) {
                stopMicrophone();
                micError = microphoneErrorText(error);
                render();
            }

            return;
        }

        if (generation !== micGeneration) {
            stream.getTracks().forEach((track) => track.stop());
            context.close();
            return;
        }

        mediaStream = stream;
        audioContext = context;

        const source = audioContext.createMediaStreamSource(mediaStream);
        inputGain = audioContext.createGain();
        inputGain.gain.value = volumePercent / 100;
        captureNode = new AudioWorkletNode(audioContext, "ks-voice-capture", { numberOfOutputs: 1 });
        captureNode.port.onmessage = (event) => onFrame(new Int16Array(event.data.frame), event.data.rms);

        // Keep the node pulled by the graph without making any sound.
        const silence = audioContext.createGain();
        silence.gain.value = 0;
        source.connect(inputGain).connect(captureNode).connect(silence).connect(audioContext.destination);

        elements.start.hidden = true;
        elements.stop.hidden = false;
        elements.device.disabled = false;

        // Started on page load, the audio graph may be held until the user interacts with the page.
        micState = audioContext.state === "running" ? "on" : "needs-gesture";
        if (micState === "needs-gesture")
            resumeOnGesture();

        render();
        populateDevices().catch(() => {
            // Only the device list is affected; the microphone itself is already running.
        });
    }

    function resumeOnGesture() {
        const resume = async () => {
            window.removeEventListener("pointerdown", resume, true);
            window.removeEventListener("keydown", resume, true);

            if (!audioContext || micState !== "needs-gesture")
                return;

            await audioContext.resume();
            if (micState === "needs-gesture" && audioContext?.state === "running") {
                micState = "on";
                render();
            }
        };

        window.addEventListener("pointerdown", resume, true);
        window.addEventListener("keydown", resume, true);

        // Some browsers release it on their own once capture is live.
        audioContext.addEventListener("statechange", () => {
            if (micState === "needs-gesture" && audioContext?.state === "running") {
                micState = "on";
                render();
            }
        });
    }

    function stopMicrophone() {
        micGeneration++;
        captureNode?.disconnect();
        captureNode = null;
        inputGain = null;
        mediaStream?.getTracks().forEach((track) => track.stop());
        mediaStream = null;
        audioContext?.close();
        audioContext = null;

        pending = [];
        preroll = [];
        gateOpenFrames = 0;
        testing = false;
        micState = "off";
        elements.meterFill.style.width = "0";
        elements.start.hidden = false;
        elements.start.disabled = terminal;
        elements.stop.hidden = true;
        render();
    }

    async function populateDevices() {
        const devices = await navigator.mediaDevices.enumerateDevices();
        const selected = mediaStream?.getAudioTracks()[0]?.getSettings().deviceId ?? "";

        elements.device.replaceChildren();
        for (const device of devices.filter((d) => d.kind === "audioinput")) {
            const option = document.createElement("option");
            option.value = device.deviceId;
            option.textContent = device.label || "Microphone";
            option.selected = device.deviceId === selected;
            elements.device.append(option);
        }
    }

    function init() {
        if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
            elements.insecure.hidden = false;
            return;
        }

        if (!token) {
            elements.noToken.hidden = false;
            return;
        }

        elements.main.hidden = false;
        setGate(Number.isFinite(gateDb) ? gateDb : -50);
        setVolume(Number.isFinite(volumePercent) ? Math.min(200, Math.max(0, volumePercent)) : 100);

        elements.gate.addEventListener("input", () => setGate(Number(elements.gate.value)));
        elements.volume.addEventListener("input", () => setVolume(Number(elements.volume.value)));
        elements.test.addEventListener("click", () => setTesting(!testing));
        elements.start.addEventListener("click", startMicrophone);
        elements.stop.addEventListener("click", stopMicrophone);
        elements.device.addEventListener("change", async () => {
            storage.set("localStorage", DEVICE_KEY, elements.device.value);
            const wasTesting = testing; // so a test can compare microphones
            stopMicrophone();
            await startMicrophone();
            if (wasTesting)
                setTesting(true);
        });

        window.addEventListener("beforeunload", () => {
            socketClosedByUs = true;
            socket?.close(1000, "page-closed");
        });

        render();
        connect();

        // Ask for the microphone straight away: the browser shows its permission prompt (or, once allowed, just
        //      starts), so opening the link is all it takes. Declining leaves the button to try again.
        startMicrophone();
    }

    init();
})();
