cmd-ks_fcadmin-desc = Opens the field commander admin window: its status, phases, assets and custom waves.
cmd-ks_fcadmin-help = ks_fcadmin

ks-fieldcommander-admin-menu-button = Field Commander
ks-fieldcommander-admin-title = Field Commander
ks-fieldcommander-admin-no-rule = No field commander is running.

ks-fieldcommander-admin-status-phase = Phase { $index }: { $phase }
ks-fieldcommander-admin-status-phase-none = Phase: not started
ks-fieldcommander-admin-status-doctrine = Doctrine: { $doctrine }
ks-fieldcommander-admin-status-doctrine-none = Doctrine: none
ks-fieldcommander-admin-status-next-wave = Next wave in { $seconds } s
ks-fieldcommander-admin-status-next-wave-none = No wave scheduled
ks-fieldcommander-admin-status-next-phase = Next phase in { $seconds } s
ks-fieldcommander-admin-status-next-phase-none = Last phase
ks-fieldcommander-admin-status-alive = Units alive: { $alive }/{ $cap }
ks-fieldcommander-admin-status-defenders = Defenders: { $defenders }
ks-fieldcommander-admin-status-budget = Carried budget: { $budget }
ks-fieldcommander-admin-status-waves = Waves sent: { $waves }, queued: { $queued }
ks-fieldcommander-admin-status-paused = { $paused ->
    [true] PAUSED
   *[false] Running
}

ks-fieldcommander-admin-pause = Pause
ks-fieldcommander-admin-unpause = Unpause
ks-fieldcommander-admin-force-wave = Force wave
ks-fieldcommander-admin-block-rcd = No RCDs in waves
ks-fieldcommander-admin-block-rcd-tooltip = While pressed, nobody can use an RCD for as long as any of the commander's units is alive.
ks-fieldcommander-admin-send-mobik = Send mobik
ks-fieldcommander-admin-send-mobik-tooltip = Announces the failed commander and sends him in as a mobik now. This begins the round's ending: no more waves, and killing him ends the round.
ks-fieldcommander-admin-refresh = Refresh
ks-fieldcommander-admin-apply = Apply

ks-fieldcommander-admin-result-accepted = Done.
ks-fieldcommander-admin-result-refused = Refused: check the values.
ks-fieldcommander-admin-result-invalid = Not sent: a field is empty or not a number.

ks-fieldcommander-admin-tab-phases = Phases
ks-fieldcommander-admin-tab-assets = Assets
ks-fieldcommander-admin-tab-custom-wave = Custom wave
ks-fieldcommander-admin-tab-queue = Queue

ks-fieldcommander-admin-phase-header-index = #
ks-fieldcommander-admin-phase-header-name = Phase
ks-fieldcommander-admin-phase-header-start = Start (s)
ks-fieldcommander-admin-phase-header-interval = Wave interval (s)
ks-fieldcommander-admin-phase-header-budget = Budget
ks-fieldcommander-admin-phase-header-apply = { "" }
ks-fieldcommander-admin-phase-header-go = { "" }
ks-fieldcommander-admin-phase-go = Go to
ks-fieldcommander-admin-phase-current = ▶ { $index }

ks-fieldcommander-admin-asset-header-index = #
ks-fieldcommander-admin-asset-header-entity = Entity
ks-fieldcommander-admin-asset-header-humanoid = Humanoid
ks-fieldcommander-admin-asset-header-count = Count
ks-fieldcommander-admin-asset-header-cost = Cost
ks-fieldcommander-admin-asset-header-weight = Weight
ks-fieldcommander-admin-asset-header-min-phase = Min phase
ks-fieldcommander-admin-asset-header-max-alive = Max alive
ks-fieldcommander-admin-asset-header-delivery = Delivery
ks-fieldcommander-admin-asset-header-living = Alive
ks-fieldcommander-admin-asset-header-actions = { "" }
ks-fieldcommander-admin-asset-header-tags = Tags
ks-fieldcommander-admin-asset-header-every = Every N
ks-fieldcommander-admin-asset-header-on-phase = Phase start
ks-fieldcommander-admin-asset-header-name = Name
ks-fieldcommander-admin-asset-header-stand-off = Stand-off
ks-fieldcommander-admin-asset-header-kill-points = Kill pts
ks-fieldcommander-admin-asset-header-orders = Orders
ks-fieldcommander-admin-asset-header-announcement = Announcement
ks-fieldcommander-admin-asset-header-intel = Intel
ks-fieldcommander-admin-asset-kill-points-placeholder = from cost
ks-fieldcommander-admin-asset-locid-placeholder = locale id, or none
ks-fieldcommander-admin-asset-tags-placeholder = infantry, pod
ks-fieldcommander-admin-asset-disable = Disable
ks-fieldcommander-admin-asset-enable = Enable
ks-fieldcommander-admin-asset-add = Add asset
ks-fieldcommander-admin-asset-add-placeholder = Entity prototype id
ks-fieldcommander-admin-asset-add-hint = Starts at count 1, cost 1, weight 1; edit it once added.
ks-fieldcommander-admin-delivery-foot = On foot
ks-fieldcommander-admin-delivery-pod = By pod

ks-fieldcommander-admin-wave-asset = Asset
ks-fieldcommander-admin-wave-count = Purchases
ks-fieldcommander-admin-wave-option = #{ $index } { $name } (×{ $count }, cost { $cost })
ks-fieldcommander-admin-wave-add-row = Add row
ks-fieldcommander-admin-wave-remove-row = Remove
ks-fieldcommander-admin-wave-announcement = Announcement, sent as written (optional):
ks-fieldcommander-admin-wave-announcement-placeholder = Leave empty for the commander's own taunt
ks-fieldcommander-admin-wave-queue = Queue
ks-fieldcommander-admin-wave-send-now = Send now

ks-fieldcommander-admin-queue-empty = Nothing is queued.
ks-fieldcommander-admin-queue-wave = { $position }. { $purchases }
ks-fieldcommander-admin-queue-purchase = { $count } × #{ $index } { $name }
ks-fieldcommander-admin-queue-announcement = "{ $text }"
ks-fieldcommander-admin-queue-remove = Remove
ks-fieldcommander-admin-queue-dropped = Field commander: a queued wave was dropped, as it could never be sent: every asset in it is disabled, or brings more than the cap of { $cap } units alive.
ks-fieldcommander-admin-list-separator = ,{" "}
