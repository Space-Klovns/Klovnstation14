# Gun audio import checklist

- [x] Repair malformed rifle/shotgun soundGunshot YAML and use one-line KS14-marked overrides.
- [x] Wire AKMS, .20 rifles/M90/XL8, Estoc, ballistic pistols, revolvers, Hristov, Mosin, L6, and both shotgun categories.
- [x] Add KS14 collections for every supplied folder used; use Crescent paths for all imported audio and Ks IDs.
- [x] Switch lethal energy guns (including selectable energy modes), excluding xray and laser cannons, to infantry plasma; set those two cannons to Pulssiase.
- [x] Reduce Cobra shot level by 50% relative amplitude and locate the .25 caseless bullet prototype impact sound; reduce impact by 75% via game volume params or a properly attenuated OGG.
- [x] Find guns with unchanged shots, report their prototype IDs and current sounds.
- [x] Inspect diffs and validate all referenced files and YAML structure.

- [x] Generate paired far-shot OGGs for every weapon sound changed, add KS14 far sound collections for them, and wire each weapon's farSoundGunshot to its matching collection.

## Validation notes

- Changed YAML files passed a YamlDotNet syntax parse.
- Collection paths were checked against the imported OGG files; Madrugad near and far were converted to mono.
- The rebuilt prototype linter reported no errors on two runs; the second included the turret changes.
- The untouched-gun inventory records inherited changes separately for review.

- The weapon-folder search for rifle, pistol, magnum, and shotgun magazine tags found the upstream rifle, pistol, shotgun and KS14 Mk1 files; the modified XL8 in the private prototype module has an explicit M16 override.
- All 28 KS14 near/far collection IDs are defined and referenced; YamlDotNet loaded all modified YAML without duplicate mapping keys.
- AudioParams documents volume in dB: Cobra uses -6.0206 dB (half amplitude) and the standard caseless bullet impact uses -12.0412 dB (quarter amplitude).
- All 43 near audio files were compared with the provided source: 42 are byte-identical, and Madrugad was downmixed from stereo to mono for positional playback.
