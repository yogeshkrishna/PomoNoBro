# PomoNoBro | Focus with Context

**Team WeKnowWhatWereDong · wHACKiest 2026 · Track 3 — Know Thyself**

PomoNoBro is a proposed Windows focus timer that uses your session goal to decide when a nudge would help. It keeps apps available so you can move between a lecture, notes and other resources during the same session.

## Example session

Set a 25-minute goal: *Study discrete mathematics on YouTube and take notes.*

| What happens | How the timer should respond |
| --- | --- |
| You watch a lecture or switch to your notes. | Continue quietly. |
| You take a brief doorbell break. | Give you time to return. |
| You watch a motivational clip you allowed as a warm-up. | Allow it within the warm-up window. |
| A gaming video stays off-goal for 45 seconds. | Play a cue such as “Back to your maths lecture?” |
| Repeated eye closure coincides with short sleep reported by a paired watch. | Offer a short break. |

## Planned MVP

- **Desktop timer:** Electron and React for the Windows app, goal entry and user-selected screen capture.
- **Local context:** Optical character recognition and a small text model compare visible screen text with the goal. MediaPipe estimates webcam cues such as head position and eye closure.
- **Optional wearable context:** A paired Android phone reads available sleep and heart-rate records through Health Connect. Wearable readings help choose the type of check-in.
- **Voice and feedback:** Cached ElevenLabs prompts can play offline. Snoozing or correcting a cue helps tune future sessions.
- **Privacy and sync:** The PC processes and discards screen and camera frames. Wearable readings stay on your devices. SQLite stores session history locally. Opt-in AWS sync carries session totals and cue feedback.

## Status

This repository documents the ideathon proposal. The first prototype will demonstrate a goal-relevant lecture, a brief interruption, and a sustained off-goal switch in one session.
