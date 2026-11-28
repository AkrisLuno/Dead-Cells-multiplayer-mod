# Native function names

## Enemy combat

- `Mob` constructor, `Mob.create`, `Mob.canUpdate`, `Entity.canUpdate`
- `Entity.applyAttackResult`, `Mob.applyAttackResult`, `AttackData.init`, `AttackData.getHitX`
- `Entity.setPosPixel`, `Entity.updatePositionXY`, `Entity.spriteUpdate`, `Mob.onDie`, `Mob.destroy`
- `Fx.hitLines`, `Fx.mobDeath`
- `Entity.set_team`, `Entity.set_targetable`, `Entity.canReceiveAttack`, `Mob.setAttackTarget`
- `AttackUtils.createFromMob`, `AttackUtils.hit`
- `Boss.isReady`, `Boss.setReady`, `Boss.bossLevel`, `Boss.onDie`, `Entity.kill`
- `Bullet.onTouchValidTarget`, `AttackData.source`, `AttackData.carrier`

## Co-op revival

- `Hero.onDie`, `Entity.kill`, `Hero.tryToPreventDeath`, `Hero.checkContinueMode`, `Hero.setLifeAndRally`, `Hero.controlsLocked`
- `Hero.lockControlsS`, `Hero.unlockControls`, `Hero.interruptHeroSkills`, `Entity.cleanDOT`
- `Hero.canReceiveAttack`, `Hero.applyAttackResult`, `Hero.updateInteraction`, `Controller.framePresses`

## Mob loot

- `Mob.dropLoot`
- `Level.dropMobLoots`
- `Level.updatedDelayedLoots`
- `Loot.pickUp`
- `ItemDrop.onDropAsLoot`

## Movement, audio and effects

- `Hero.landStun`
- `Hero.onLand`
- `Hero.playStepSound`
- `Hero.playClimbSfx`
- `stopWithStateAnims`
- `hasAffect`
- `LevelMap.isWetGround`
- `LevelAudio.playEventAt`
- `LevelAudio.playEventOn`
- `DiveAttack.onOwnerLand`
- `ItemMeta.hasMeta`
- `FlashLight.create`
- `Viewport.shakeS`
- `Fx.landSmoke`
- `Fx.landDirt`
- `Fx.landHeavy`
- `DodgeRoll.onStart`
- `Fx.dodgeStart`
- `Fx.driftDirtAndSmoke`
- `Fx.dash`
- `Fx.customMask`
- `Fx.diveAttack`
- `Weapon.dynOnFxFrame`
- `Weapon.playReleaseSfx`
- `Fx.playWeaponAnim`
- `Fx.playWeaponAnimFromObject`
- `Fx.playWeaponFx`
- `OldSkill.playSfx`
- `Fx.frontShield`, `Fx.shieldParryStart`, `Fx.frontShieldHit`
- `Fx.healing`, `Fx.flaskHeal`, `Fx.damageAura`, `Fx.entityHorizontalTeleport`
- `Grenade.fixedUpdate`, `Bullet.fixedUpdate`, `Active.fixedUpdate`, `SmokeBomb.finishCast`
- `Fx.radiusExplosion`, `Fx.deflagration`, `Fx.oilDeflagration`, `Fx.frostExplosion`, `Fx.smokeBombGrenade`
- Constructors for `Bullet`, `Grenade`, `Active`, `Pet`, `PetMob`, `Boomerang`, `Decoy` and `Homunculus`
- `Fx.tailLine`, `Fx.tailLineFree`, `Fx.petSpawn`, `Fx.smallIceExplosion`, `Fx.dirtExplosion`
- `Owl.init`, `Owl.onBuff`, `Owl.onDebuff`, `Owl.depop`
- `FlyingSword.init`, `FlyingSword.onBuff`, `FlyingSword.onDebuff`, `FlyingSword.hitCurrentTarget`, `FlyingSword.depop`
- `PetMob.depop`, `S_ProtectorMushroom.mushExplose`, `Fx.bloodHit`, `Fx.hitLines`
- `GiantWhistle.onCooldownEnd`, `GiantWhistle.shoryuken`, `Fx.giantShoryuken`, `Fx.groundStones`

## Shop reroll

- `ShopReroll.canBeActivated`
- `ShopReroll.onActivate`
- `ShopReroll.isValidItem`
- `ShopReroll.destroyWithFx`
- `ShopBooth` constructor
- `ShopBooth.replaceItem`
- `Entity.onActivate`
- `Entity.init`
- `Entity.destroy`
- `Entity.popError`
- `Entity.popText`
- `Hero.substractMoney`
- `Fx.refusal`
- `Fx.shopReroll`
- `LevelAudio.playEvent`
- `LevelMap.getRoomAt`
- `InventItem.isGroup`
- `InventItem.addAffix`
- `InventItem.getDefaultPriceWithAffixes`
- `ArrayObj.splice`
- `GetText.get`
- `IntMap.get`
- `hxd.Res.load`
- `hxd.res.Loader.loadCache`
- `toSound`
