"use strict";

// Voice chat microphone page. See Klovn/Docs/voice_chat.md for the protocol and the security model.

(() => {
    const PROTOCOL_VERSION = 1;
    const FRAME_SAMPLES = 320;          // 20 ms at 16 kHz
    const FRAMES_PER_MESSAGE = 3;       // 60 ms per websocket message
    const PREROLL_FRAMES = 2;           // sent when the gate opens, so word onsets aren't clipped
    const HANGOVER_FRAMES = 15;         // 300 ms of gate hold after the level drops
    const PING_INTERVAL_MS = 20000;
    const MAX_RECONNECTS = 5;
    const TOKEN_KEY = "ksVoiceToken";
    const GATE_KEY = "ksVoiceGateDb";
    const DEVICE_KEY = "ksVoiceDevice";

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

    let sequence = 0;
    let gateOpenFrames = 0;
    let pending = [];
    let preroll = [];
    let gateDb = Number(storage.get("localStorage", GATE_KEY) ?? -50);

    function setStatus(state, text) {
        elements.status.dataset.state = state;
        elements.statusText.textContent = text;
    }

    function setGate(value) {
        gateDb = value;
        elements.gate.value = String(value);
        elements.gateValue.textContent = `${value} dB`;
        elements.meterGate.style.left = `${dbToMeter(value) * 100}%`;
        storage.set("localStorage", GATE_KEY, String(value));
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
            if (TERMINAL_REASONS.has(reason) || CLOSE_TEXT[reason]) {
                terminal = TERMINAL_REASONS.has(reason);
                setStatus("error", CLOSE_TEXT[reason] ?? `Disconnected (${reason}).`);
                elements.identity.textContent = "Not connected.";
                if (terminal) {
                    stopMicrophone();
                    return;
                }
            }

            if (reconnects >= MAX_RECONNECTS) {
                setStatus("error", "Lost connection to the server. Reload the page to try again.");
                return;
            }

            reconnects++;
            setStatus("waiting", "Reconnecting…");
            setTimeout(connect, 1000 * reconnects);
        });
    }

    function handleServerMessage(message) {
        switch (message.type) {
            case "hello":
                reconnects = 0;
                elements.identity.textContent = `Connected as ${message.name}.`;
                setStatus("waiting", REASON_TEXT["not-holding-key"]);
                break;
            case "state":
                if (message.transmitting) {
                    setStatus("transmitting", "Transmitting in-game.");
                } else {
                    let text = REASON_TEXT[message.reason] ?? "Not transmitting.";
                    if (typeof message.seconds === "number" && message.seconds > 0)
                        text += ` (${message.seconds}s left)`;

                    setStatus(message.reason === "not-holding-key" ? "waiting" : "blocked", text);
                }
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

        if (db >= gateDb)
            gateOpenFrames = HANGOVER_FRAMES;
        else if (gateOpenFrames > 0)
            gateOpenFrames--;

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

        if (preroll.length > 0) {
            pending.push(...preroll);
            preroll = [];
        }

        pending.push(frame);
        while (pending.length >= FRAMES_PER_MESSAGE) {
            sendFrames(pending.slice(0, FRAMES_PER_MESSAGE));
            pending = pending.slice(FRAMES_PER_MESSAGE);
        }
    }

    async function startMicrophone() {
        elements.start.disabled = true;

        try {
            const deviceId = elements.device.value || storage.get("localStorage", DEVICE_KEY) || undefined;
            mediaStream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    deviceId: deviceId ? { ideal: deviceId } : undefined,
                    channelCount: 1,
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true,
                },
            });
        } catch (error) {
            elements.start.disabled = false;
            setStatus("error", `Couldn't open the microphone: ${error.message || error.name}`);
            return;
        }

        audioContext = new AudioContext();
        await audioContext.audioWorklet.addModule("worklet.js");

        const source = audioContext.createMediaStreamSource(mediaStream);
        captureNode = new AudioWorkletNode(audioContext, "ks-voice-capture", { numberOfOutputs: 1 });
        captureNode.port.onmessage = (event) => onFrame(new Int16Array(event.data.frame), event.data.rms);

        // Keep the node pulled by the graph without making any sound.
        const silence = audioContext.createGain();
        silence.gain.value = 0;
        source.connect(captureNode).connect(silence).connect(audioContext.destination);

        await populateDevices();
        elements.start.hidden = true;
        elements.stop.hidden = false;
        elements.device.disabled = false;

        if (!socket && !terminal)
            connect();
        else
            setStatus("waiting", REASON_TEXT["not-holding-key"]);
    }

    function stopMicrophone() {
        captureNode?.disconnect();
        captureNode = null;
        mediaStream?.getTracks().forEach((track) => track.stop());
        mediaStream = null;
        audioContext?.close();
        audioContext = null;

        pending = [];
        preroll = [];
        gateOpenFrames = 0;
        elements.meterFill.style.width = "0";
        elements.start.hidden = false;
        elements.start.disabled = terminal;
        elements.stop.hidden = true;
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

        elements.gate.addEventListener("input", () => setGate(Number(elements.gate.value)));
        elements.start.addEventListener("click", startMicrophone);
        elements.stop.addEventListener("click", () => {
            stopMicrophone();
            setStatus("idle", "Microphone stopped.");
        });
        elements.device.addEventListener("change", async () => {
            storage.set("localStorage", DEVICE_KEY, elements.device.value);
            stopMicrophone();
            await startMicrophone();
        });

        window.addEventListener("beforeunload", () => {
            socketClosedByUs = true;
            socket?.close(1000, "page-closed");
        });

        connect();
    }

    init();
})();
