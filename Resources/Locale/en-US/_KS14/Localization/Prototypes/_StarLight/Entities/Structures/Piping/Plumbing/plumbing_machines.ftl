# Resources\Prototypes\_StarLight\Entities\Structures\Piping\Plumbing\plumbing_machines.yml
ent-PlumbingDrain = plumbing drain
    .desc = A floor drain that absorbs puddles and routes them into the plumbing network instead of disposing them.

# Resources\Prototypes\_StarLight\Entities\Structures\Piping\Plumbing\plumbing_machines.yml
ent-PlumbingFilter = plumbing filter
    .desc = Filters specific reagents. Pulls from inlet (north). Filtered reagents at west output, non-filtered at south output.

# Resources\Prototypes\_StarLight\Entities\Structures\Piping\Plumbing\plumbing_machines.yml
ent-PlumbingFilterFlipped = plumbing filter
    .desc = Filters specific reagents. Pulls from inlet (south). Filtered reagents at west output, non-filtered at north output.
    .suffix = Flipped

# Resources\Prototypes\_StarLight\Entities\Structures\Piping\Plumbing\plumbing_machines.yml
ent-PlumbingInput = plumbing input
    .desc = A small tank you pour reagents into. Other machines pull from this. Connects in all directions.

# Resources\Prototypes\_StarLight\Entities\Structures\Piping\Plumbing\plumbing_machines.yml
ent-PlumbingMachineBase =
    .desc = { "" }

# Resources\Prototypes\_StarLight\Entities\Structures\Piping\Plumbing\plumbing_machines.yml
ent-PlumbingOutput = plumbing output
    .desc = A small tank that pulls reagents from the network. Draw from it with a container. Connects in all directions.

# Resources\Prototypes\_StarLight\Entities\Structures\Piping\Plumbing\plumbing_machines.yml
ent-PlumbingPillPress = plumbing pill press
    .desc = Pulls reagents from the network and automatically presses them into pills or patches at a configurable dosage. Has optional mixing inlets (E/W) for ratio-controlled mixing.

# Resources\Prototypes\_StarLight\Entities\Structures\Piping\Plumbing\plumbing_machines.yml
ent-PlumbingReactor = plumbing reactor
    .desc = Pulls target reagents from inlet (north) and triggers reactions when targets are met. Products available at outlet (south).

# Resources\Prototypes\_StarLight\Entities\Structures\Piping\Plumbing\plumbing_machines.yml
ent-PlumbingSink = plumbing sink
    .desc = A sink connected to the plumbing network. Draw reagents from it with a container, or dump reagents into it to send them down the drain. Connects in all directions.

# Resources\Prototypes\_StarLight\Entities\Structures\Piping\Plumbing\plumbing_machines.yml
ent-PlumbingSmartFridge = plumbing smart fridge
    .desc = A plumbing-connected smart fridge. Pulls all reagents from the network and stores up to 200u of each. Insert a labeled jug to fill it with the matching reagent.

# Resources\Prototypes\_StarLight\Entities\Structures\Piping\Plumbing\plumbing_machines.yml
ent-PlumbingSynthesizer = plumbing synthesizer
    .desc = Generates reagents using power. Select a reagent and it fills its buffer for other machines to pull from. Output at south.

# Resources\Prototypes\_StarLight\Entities\Structures\Piping\Plumbing\plumbing_machines.yml
ent-PlumbingSynthesizerWater = Plumbing Synthesizer
    .desc = Generates basic reagents using power from an internal battery.
    .suffix = Water

# Resources\Prototypes\_StarLight\Entities\Structures\Piping\Plumbing\plumbing_machines.yml
ent-PlumbingTank = plumbing tank
    .desc = A tank that pulls reagents from its inlet (north) and stores up to 800u. Other machines can pull from its outlets (south, east, west).
