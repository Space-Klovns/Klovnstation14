ks-shotdiag-command-desc = Prints what decides whether a shot hits the mob under the cursor, as your client has it and as the server has it, marking the differences. For when your bullets fly through someone the server's hit.
ks-shotdiag-command-help = Usage: ks_shotdiag [net entity]. With no entity, the mob under the cursor, or the one nearest it. Bind it to a key to use it mid-fight: bind F6 Command ks_shotdiag
ks-shotdiag-command-bad-target = '{ $target }' is not a net entity that exists.
ks-shotdiag-command-no-target = No mob under or near the cursor.
ks-shotdiag-command-header = Shot diagnostics for { $entity } at client tick { $tick }
ks-shotdiag-command-refused = The server did not answer: the entity is gone there, or you lack the debug admin flag. Only this client's values are shown.
ks-shotdiag-command-differences = { $count ->
    [0] The two sides agree.
    *[other] { $count } differences, marked with !
}
