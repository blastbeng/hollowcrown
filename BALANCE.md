# BALANCE — HOLLOWCROWN

All tunable numbers live here (Vision 7/8/9). Every combat change ends with a
harness run (Vision 7); harness arrives with the balance-harness task.

## Movement + stamina (Vision 7 shared rules; PlayerController.cs)
| name            | value  | notes                              |
|-----------------|--------|------------------------------------|
| walk_speed      | 4.5 m/s| base WASD, camera-relative          |
| sprint_speed    | 7.0 m/s| hold Shift, drains stamina          |
| dodge_speed     | 9.5 m/s| burst, no steering during roll      |
| dodge_duration  | 0.3 s  | == i-frame window (Vision 7)        |
| dodge_cooldown  | 0.9 s  | between rolls                       |
| dodge_cost      | 25     | stamina                             |
| stamina_max     | 100    | Vision 7                            |
| sprint_drain    | 20/s   | empty at 5 s of sprinting           |
| stamina_regen   | 15/s   | after 1.0 s idle delay              |
| accel           | 18/s   | velocity approach rate              |

## Combat (to be filled by the combat core task)
Server-authoritative damage, telegraphs 0.5-0.8 s, block -70%, parry window
0.25 s -> riposte (Vision 7). Class kits + numbers land with each class task.

## Warden chain (Vision 7 kit slice 1; WardenChain.cs)
| name          | value    | notes                                   |
|---------------|----------|-----------------------------------------|
| chain_damage  | 20/20/35 | 3-hit arc; finisher heavier             |
| chain_reach   | 2.4 m    | ground-projected arc sector             |
| chain_arc     | 120 deg  | sector centered on player, aims cursor  |
| combo_window  | 0.9 s    | press again inside window to advance    |
| full_chain    | 75 dmg   | kills a 100 HP dummy in 2 chains        |
| attack_inputs | Q / LMB  | action "attack"                         |

## Warden kit (Vision 7 kit slice 2; WardenKit.cs)
| name          | value    | notes                                   |
|---------------|----------|-----------------------------------------|
| shield_bash   | E        | 90 deg cone x 3.2 m, 15 dmg, 0.5 s stun |
| bash_cost     | 20       | stamina; cooldown 6 s                   |
| warcry        | R        | +15% chain damage, radius 8 m, 10 s     |
| warcry_cd     | 12 s     | no stamina cost                         |
| shield_wall   | F        | 100% block 2 s (Vision 7)               |
| wall_drain    | 25/s     | stamina; wall ends at empty or 2 s      |
| inputs        | E / R / F| Vision 1 lists QWER but W is movement — skills bind Q / E / R / F |

## Nightblade kit (Vision 7 kit slice; NightbladeChain.cs + NightbladeKit.cs)
| name           | value     | notes                                        |
|----------------|-----------|----------------------------------------------|
| chain_damage   | 14/14/28  | fast 3-hit stab chain; finisher heavier       |
| chain_reach    | 2.0 m     | shorter than the warden arc                   |
| chain_arc      | 100 deg   | tight twin-dagger sector                      |
| combo_window   | 0.7 s     | faster chain rhythm                           |
| full_chain     | 56 dmg    | vs warden 75 — speed over burst               |
| min_interval   | 0.18/0.18/0.35 s | server anti-spam floor per swing       |
| attack_inputs  | Q / LMB   | action "attack"                               |
| shadow_step    | E         | 6 m blink toward cursor                       |
| step_cost      | 15        | stamina; cooldown 8 s                         |
| stealth        | R         | 5 s ghost, breaks on attack, next hit +50%    |
| stealth_cd     | 12 s      | server-enforced per peer                      |
| smoke_bomb     | F         | radius 3.5 m, 6 s, throws to cursor (10 m)    |
| smoke_cost     | 25        | stamina; cooldown 8 s (server-enforced)       |
| smoke_blind    | server    | hits out of/through the cloud are REJECTED    |
| stealth_bonus  | x1.50     | applied server-side on the breaking hit       |
| nightblade_model | 1.80 m  | BodyScale 1.12 — slim twin-dagger silhouette  |

## Revenant kit, slice 1 (Vision 7; RevenantChain.cs + RevenantKit.cs)
| name           | value    | notes                                          |
|----------------|----------|------------------------------------------------|
| bone_spear     | Q        | 18 dmg, 9 m x 1.2 m ground LINE (line hitbox)  |
| spear_shape    | instant  | line applies on cast; bolt visual travels 0.35 s |
| spear_cooldown | 5 s      | server floor 4.5 s                             |
| grave_grasp    | E        | 6 dmg + 1.0 s ROOT, 4.5 m circle at the cursor |
| grasp_cost     | 20       | stamina; cooldown 9 s (server floor 8.5 s)     |
| root_semantics | 1.0 s    | rooted cannot move/dodge, CAN still fight      |
| revenant_model | 1.85 m   | staff + hood variant (in WardenModel pipeline) |
| slice 2        | R + F    | life drain (channel line) + soul ward (absorb) |

## Revenant kit, slice 2 (Vision 7; RevenantKit.cs — server fields in CombatTables)
| name            | value     | notes                                            |
|-----------------|-----------|--------------------------------------------------|
| life_drain      | R         | 2 s channel: 4 ticks x 8 dmg along 6 m x 1.2 m   |
| drain_leech     | 50%       | heals the caster per tick, server-owned, caps max |
| drain_cost      | 20        | stamina; cooldown 12 s                           |
| drain_breaks    | death/stun| channel stops (movement allowed)                 |
| soul_ward       | F         | absorbs up to 40 dmg BEFORE HP, lasts 8 s max    |
| ward_cooldown   | 12 s      | server-enforced; pool server-owned               |
| ward_visual     | arcane disc| under the caster while the pool is up           |

## Training dummy (TrainingDummy.cs — combat verification target)
| name         | value | notes                            |
|--------------|-------|----------------------------------|
| dummy_hp     | 100   | respawn 3.0 s after death        |

## Players (server-authoritative — CombatAuthority.cs / PlayerController.cs)
| name         | value | notes                                       |
|--------------|-------|---------------------------------------------|
| player_hp    | 100   | authority-owned; clients mirror only        |
| player_respawn | 3.0 s| at the peer's spawn point (server broadcast)|
| buff_cap     | 1.25  | sane-cap anti-cheat on RequestBuff          |
| position_sync | 10 Hz| client->server unreliable, relayed to peers |

## Balance harness (Vision 7; CombatBot.cs + tools/balance_harness.sh)
| name            | value   | notes                                          |
|-----------------|---------|------------------------------------------------|
| bot_ids         | 500+    | offline bots; world targets start at 1000       |
| warden_bot      | 1.0 s   | chain 1/2/3 (20/20/35), reach 2.2 m             |
| nightblade_bot  | 0.6 s   | dagger 5/6/7 (14/14/28), reach 1.8 m            |
| revenant_bot    | 5.0 s   | bone spear only (18, 9 m line), reach 8.5 m     |
| bot_move_speed  | 4.5 m/s | approach; fight starts inside reach*0.8         |
| harness_run     | 25 s    | tools/balance_harness.sh [seconds] [a+b]        |

### Matrix runs (KILL lines from the authority log)
| date       | matchup              | result            | verdict              |
|------------|----------------------|-------------------|----------------------|
| 2026-09-05 | warden vs nightblade | 2 : 2 (25 s run)  | even — in band       |

Interpretation: with NO dodges/blocks/kits the raw chain trade is even
(nightblade cadence 0.6 s x 14/28 vs warden 1.0 s x 20/35). Matchups with
driven skill use (dodge/parry/kits) need the playtester harness; the headless
matrix proves the damage engine and class scaling, not full kit balance.
Target per matchup (Vision 7): 45-55% once mode scoring lands.

