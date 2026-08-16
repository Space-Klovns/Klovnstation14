## Datalink UI

ks-datalink-transmitter-ui-title = Datalink Transmitter
ks-datalink-receiver-ui-title = Datalink Receiver

ks-datalink-ui-status-label = Status
ks-datalink-ui-status-off = OFF
ks-datalink-ui-status-unpowered = NO POWER
ks-datalink-ui-status-transmitting = TRANSMITTING
ks-datalink-ui-status-silent = SILENT (0%)
ks-datalink-ui-status-listening = LISTENING

ks-datalink-ui-toggle-button = Toggle
ks-datalink-ui-frequency-label = Frequency
ks-datalink-ui-frequency-all = ALL CHANNELS
ks-datalink-ui-power-label = Transmit power
ks-datalink-ui-power-value = { $percent }%
ks-datalink-ui-range-label = Effective range
ks-datalink-ui-range-value = { $range } m
ks-datalink-ui-range-unlimited = SECTOR-WIDE
ks-datalink-ui-heard-label = Links heard

ks-datalink-examine-frequency = It's tuned to frequency { $frequency }.

## Sensor examine

ks-sensor-examine-operational = [color=green]The sensor array is tracking normally.[/color]
ks-sensor-examine-off = [color=gray]The sensor array is switched off.[/color]
ks-sensor-examine-unpowered = [color=red]The sensor array has no power.[/color]
ks-sensor-examine-not-mounted = [color=yellow]The sensor array has no clear view of space. It must be anchored next to at least one open tile.[/color]

## Sensor intel readouts

ks-sensor-intel-size-label = SIZE
ks-sensor-intel-mass-label = MASS
ks-sensor-intel-topspeed-label = TOP SPEED
ks-sensor-intel-heat-label = SIGNATURE
ks-sensor-intel-rcs-label = RCS
ks-sensor-intel-emitter-range-label = RADAR RANGE

ks-sensor-intel-size-small = LIGHT
ks-sensor-intel-size-medium = MEDIUM
ks-sensor-intel-size-large = HEAVY
ks-sensor-intel-size-massive = MASSIVE

ks-sensor-intel-mass-value = { $value }t
ks-sensor-intel-topspeed-value = { $value }
ks-sensor-intel-topspeed-none = NONE
ks-sensor-intel-heat-value = { $value }
ks-sensor-intel-rcs-value = { $value }
ks-sensor-intel-emitter-range-value = { $value }m

# Placeholder for a roster label whose value no sensor has detected (blank slot).
ks-sensor-intel-no-value = ---

## Radar/console contact display

ks-sensor-contact-unknown = UNKNOWN
ks-sensor-contact-last-seen = last seen { $seconds }s ago
ks-sensor-contact-live = TRACKING
ks-sensor-contact-pos-range = ({ $x }, { $y }) · { $range }m
# A bearing-only contact: direction known, position withheld. Compass degrees, 000 = north.
ks-sensor-contact-bearing = BRG { $deg }
ks-sensor-contact-source-line = { $sensor } - { $grid }
ks-sensor-contact-hops = relay x{ $hops }

## Radar overlay toggles

ks-sensor-coverage-visual-toggle = Visual Cones: { $mode }
ks-sensor-coverage-irst-toggle = IRST Cones: { $mode }
ks-sensor-coverage-radar-toggle = Radar Cones: { $mode }
ks-sensor-coverage-jammer-toggle = Jammer Cones: { $mode }
ks-sensor-coverage-mode-off = OFF
ks-sensor-coverage-mode-outline = OUTLINE
ks-sensor-coverage-mode-filled = FILLED
ks-sensor-info-toggle = Contact Info: { $mode }
ks-sensor-info-mode-off = OFF
ks-sensor-info-mode-basic = BASIC
ks-sensor-info-mode-full = FULL

ks-sensor-radar-toggle-on = RADAR: ON
ks-sensor-radar-toggle-off = RADAR: OFF

ks-sensor-jammer-toggle-on = JAMMER: ON
ks-sensor-jammer-toggle-off = JAMMER: OFF

## Jamming

ks-sensor-jammed = ! JAMMED !

## Emitter bands (identification intel read back by ELINT)

ks-emitter-band-low = LOW BAND
ks-emitter-band-mid = MID BAND
ks-emitter-band-high = HIGH BAND
ks-emitter-band-unknown = ---

ks-emitter-pattern-continuous = CONTINUOUS
ks-emitter-pattern-unknown = ---

## Bearing stability

ks-sensor-stability-unknown = ---
ks-sensor-stability-stable = STABLE
ks-sensor-stability-drifting = DRIFTING
ks-sensor-stability-fix = FIX

## Instrument shell (KS console window)

ks-instrument-window-title = SENSOR SUITE
ks-instrument-window-close = X
ks-instrument-window-pop = POP
ks-instrument-window-pop-hint = MOVE THE SUITE TO ITS OWN WINDOW - CLOSING IT BRINGS THE SUITE BACK
ks-instrument-tab-radar = RADAR
ks-instrument-tab-map = MAP
ks-instrument-tab-esm = ESM
ks-instrument-tab-dock = DOCK
ks-instrument-tab-separator = //

ks-instrument-status-contacts = CON {$count}
ks-instrument-status-emitters = EMT {$count}
ks-instrument-status-radar-on = RDR ON
ks-instrument-status-radar-off = RDR OFF
ks-instrument-status-radar-none = RDR ---
ks-instrument-status-jammer-on = JAM ON
ks-instrument-status-jammer-off = JAM OFF
ks-instrument-status-jammer-none = JAM ---
ks-instrument-status-jammed = ! JAMMED !

## ESM screen (merged ELINT + RWR picture; the analysis and warning strings below
## keep their original ks-elint-*/ks-rwr-* ids)

ks-esm-panel-plot = BEARING PLOT
ks-esm-panel-roster = EMITTERS
ks-esm-panel-threat = WARNING RECEIVER
ks-esm-panel-selected = SIGNAL ANALYSIS
ks-esm-panel-log = EMISSION LOG

ks-esm-chip-no-rwr = NO RWR RECEIVER
ks-esm-chip-no-elint = NO ELINT ARRAY
ks-esm-chip-elint-deaf = ELINT DEAF - OWN EMISSIONS
ks-esm-roster-offline = NO ESM RECEIVERS

ks-elint-roster-empty = NO EMISSIONS
ks-elint-roster-live = LIVE
ks-elint-roster-memory = MEM

ks-elint-selected-none = NO EMITTER SELECTED
ks-elint-field-designation = DESIG
ks-elint-field-classification = CLASS
ks-elint-field-band = BAND
ks-elint-field-pattern = PATTERN
ks-elint-field-signal = SIGNAL
ks-elint-field-signal-value = {$percent}%
ks-elint-field-stability = BEARING
ks-elint-field-bearing-value = {$deg} {$stability}
ks-elint-field-last-seen = LAST
ks-elint-field-last-seen-value = {$seconds}s AGO
ks-elint-field-last-seen-now = NOW
ks-elint-field-analysis = ANALYSIS
ks-elint-field-analysis-value = {$percent}%
ks-elint-field-analysis-idle = ---
ks-elint-field-no-value = ---

ks-elint-class-radar = RADAR
ks-elint-class-jammer = JAMMER
ks-elint-class-unknown = EMITTER

ks-elint-focus-button = FOCUS
ks-elint-clear-focus-button = CEASE

## MAP screen (sector chart)

ks-map-panel-chart = SECTOR CHART
ks-map-panel-ftl = FTL
ks-map-panel-settings = SETTINGS
ks-map-panel-objects = OBJECTS
ks-map-sensors-button = SENSORS
ks-map-sensors-hint = OVERLAY THE DATALINK NETWORK'S COVERAGE CONES AND LIVE CONTACTS. ALLIES STAY CHARTED EITHER WAY

## Emission log (ESM screen's log panel)

ks-emission-log-empty = NO LOG ENTRIES
# Every log row: round-time stamp first, then the event text below.
ks-emission-log-line = {$stamp} {$event}
ks-emission-log-emitter-new = + {$label} ACQUIRED
ks-emission-log-emitter-silent = - {$label} SILENT
ks-emission-log-jam-start = ! OWN RADAR JAMMED
ks-emission-log-jam-end = OWN RADAR CLEAR
ks-emission-log-unknown-emitter = EMITTER

## RWR warning strings (used by the ESM screen)

ks-rwr-threats-empty = NO ILLUMINATION
ks-rwr-threat-bearing = {$deg}

ks-rwr-field-posture = POSTURE
ks-rwr-field-priority = PRIORITY
ks-rwr-field-no-value = ---

## Threat channels + postures (RWR grouping, data-driven)

ks-threat-channel-search = SEARCH
ks-threat-channel-jam = JAM

ks-posture-calm = CALM
ks-posture-caution = CAUTION
ks-posture-danger = DANGER

## RADAR screen (nav tab)

ks-radar-panel-plot = RADAR PLOT
ks-radar-panel-ship = SHIP
ks-radar-panel-status = STATUS
ks-radar-panel-emissions = EMISSIONS
ks-radar-panel-display = DISPLAY

ks-radar-field-position = POSITION
ks-radar-field-orientation = HEADING
ks-radar-field-linear-velocity = VELOCITY
ks-radar-field-angular-velocity = TURN RATE

ks-radar-field-posture = POSTURE
ks-radar-field-threats = THREATS
ks-radar-field-contacts = CONTACTS
# live over total, e.g. "02/05"
ks-radar-field-contacts-value = {$live}/{$total}
ks-radar-field-emitters = EMITTERS
ks-radar-field-no-value = ---

## DOCK screen (docking tab)

ks-dock-panel-plot = DOCKING VIEW
ks-dock-panel-ports = DOCKING PORTS
ks-dock-ports-empty = NO DOCKING PORTS
