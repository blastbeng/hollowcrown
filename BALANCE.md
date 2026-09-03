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

