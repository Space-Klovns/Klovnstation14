# Guns retaining their existing shot sounds

This inventory covers handheld gun prototypes under `Resources/Prototypes/Entities/Objects/Weapons/Guns` and the KS14 gun folder. Variants inherit the listed weapon unless they override its Gun component. The inventory also includes handheld launchers and gun-shaped tools.

- Submachine guns: Atreides (`WeaponSubMachineGunAtreides`), C-20r and C-20r ROW (`WeaponSubMachineGunC20r`, `WeaponSubMachineGunC20rROW`), Drozd (`WeaponSubMachineGunDrozd`), WT550 (`WeaponSubMachineGunWt550`), and briefcase gun (`WeaponSubMachineGunBriefcase`).
- Heavy machine guns: minigun (`WeaponMinigun`).
- Muzzle loaders: musket (`Musket`) and flintlock pistol (`WeaponPistolFlintlock`, `WeaponPistolFlintlockEmpty`).
- Energy and less-lethal weapons: disablers (`WeaponDisablerPractice`, `WeaponDisabler`, `WeaponDisablerSMG`), tasers (`WeaponTaser`, `WeaponTaserSuper`), C.H.I.M.P. (`WeaponPistolCHIMP`, `WeaponPistolCHIMPUpgraded`), and the cyborg disabler (`WeaponDisablerBorg`).
- Suppressed shotgun: Hushpup (`WeaponShotgunHushpup`).

The handmade, blunderbuss, and improvised shotguns inherit the changed base shotgun sound. The practice laser carbine inherits the changed laser carbine sound. Review those inherited changes if they should keep their earlier sound.

- Launchers: China Lake (`WeaponLauncherChinaLake`), Hydra (`WeaponLauncherHydra`, `WeaponLauncherHydraLethal`), RPG-7 (`WeaponLauncherRocket`), multiple rocket launcher (`WeaponLauncherMultipleRocket`), pirate cannon (`WeaponLauncherPirateCannon`), and tether, force, and grappling guns (`WeaponTetherGun`, `WeaponForceGun`, `WeaponGrapplingGun`).
- Other tools include the proto-kinetic accelerator (`WeaponProtoKineticAccelerator`), flare gun (`WeaponFlareGun`, `WeaponFlareGunSecurity`), pneumatic cannons (`WeaponImprovisedPneumaticCannon`, `WeaponImprovisedPneumaticCannonGun`, `WeaponImprovisedPneumaticCannonAdmeme`), water guns, syringe guns, bows, and magical staves.

Existing shot files for common unchanged firearms (under `/Audio/Weapons/Guns/Gunshots/`): Atreides and Drozd use `atreides.ogg`; C-20r and C-20r ROW use `c-20r.ogg`; WT550 inherits `smg.ogg`; the briefcase gun uses `atreides.ogg`; minigun uses `minigun.ogg`; Hushpup uses `hushpup.ogg`; musket and flintlock inherit `sniper.ogg`. China Lake and Hydra use `grenade_launcher.ogg`; the RPG-7 and multiple rocket launcher use `rpgfire.ogg`; pirate cannon uses `mateba.ogg`.

- The KS14 AT4 (`KsWeaponLauncherRocketAt4`) retains its launcher sound. The projectile practice carbine (`WeaponLaserCarbineProjectilePractice`) retains its original `EnergyShot` collection.

Additional unchanged tool and novelty prototypes: `WeaponSprayNozzle`, `WeaponWaterPistol`, `WeaponWaterBlaster`, `WeaponWaterBlasterSuper`, `BowImprovised`, `WeaponStaffHealing`, `WeaponStaffPolymorphDoor`, `WeaponWandPolymorphCarp`, `WeaponWandPolymorphMonkey`, `WeaponWandFireball`, `WeaponWandLocker`, `WeaponWandDeath`, `WeaponWandPolymorphDoor`, `WeaponWandCluwne`, `WeaponWandPolymorphBread`, `WeaponTetherGunAdmin`, `WeaponForceGunAdmin`, `WeaponLauncherAdmemeMeteorLarge`, and `WeaponLauncherAdmemeImmovableRodSlow`.

Additional unchanged equipment: syringe gun (`LauncherSyringe`), syringe shotgun (`WeaponSyringeShotgun`), pie cannons (`LauncherCreamPie`, `BorgLauncherCreamPie`), and tile guns (`WeaponTileGun`, `WeaponTileGunEmpty`).

- Ballistic and xeno turrets retain their original shots: `WeaponTurretSyndicate`, `WeaponTurretSyndicateDisposable`, `WeaponTurretNanoTrasen`, `WeaponTurretHostile`, `WeaponTurretAllHostile`, and `WeaponTurretXeno`.
