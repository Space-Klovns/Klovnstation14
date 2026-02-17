cmd-addjoint-desc = Adds a joint of a specified type between two entities. Capitalisation matters when specifying joint type. You can specify whether the connected entities should collide with each other or not (defaults to no).
cmd-addjoint-help = insertintospeczone <first UID [uid]> <second UID [uid]> <joint type [string]> <joint collision [bool, optional]>

cmd-addjoint-invalid-args = Expected exactly either 3 or 4 arguments.
cmd-addjoint-bad-uid = {$alleged} is not a valid entity.
cmd-addjoint-bad-bool = {$alleged} is not a boolean value (true/false).
cmd-addjoint-bad-joint = {$name} is an invalid/unsupported type of joint.
cmd-addjoint-fake-uid = Entity {$uid} does not exist.

cmd-addjoint-uid-completion = <EntityUid>
cmd-addjoint-joint-completion = <Joint type>
cmd-addjoint-bool-completion = <Boolean>
