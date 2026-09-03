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
