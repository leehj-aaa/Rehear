# Audience backchannel review — 2026-09-08

Reference: [Figma audience state specification](https://www.figma.com/design/t15qxZbFhezXWMyay2REVH/Re-hear-XR?node-id=223-75).

Implemented and locally verified:

- Keep the evaluated Body core underneath temporary actions; return to that core after an action rather than always BL_03. New evaluations can interrupt current actions.
- Preserve all currently contributing pose weights on interruption. A repeated target refresh does not restart the blend clock. Completed non-looping clips restart through a new crossfade input; imported looping clips keep playing.
- Randomize initial phase only for looping core clips, not authored one-shot hand/prop actions.
- Distribute actors over a 0.85-second response window, with one common offset per actor's Face/Body/Gaze and matching offsets for the rear conversation pair. This is presentation staggering, NOT independent evaluation.
- Preserve relative command timing when a fresh STT/LLM response arrives after its original timestamp. New responses supersede older scheduled reactions for the same actor. Priority resolves collisions within a response, not against newer evaluations.
- Periodic audio is no longer unconditionally labeled utterance_boundary. Estimate during_speech/boundary/pause from trailing 20-ms RMS windows (180/600 ms starting thresholds). Explicit pause/end context is retained. This is an acoustic heuristic, not linguistic sentence detection.
- Read only the audio segment from Unity's microphone ring rather than allocating/copying the full two-minute ring at every flush.
- Server selection prefers unused eligible core candidates, caps identical core selections at two per response, and avoids repeated temporary actions (except the conversation pair). Rotate first-choice order to avoid starving the same actors. Hard state, timing, cooldown and seating gates remain authoritative.

Validation:

- Six real presentation prefabs: interrupted A→B→C arrival pose/weight continuity; normalization; repeated-target progress; persistent core; overlay return; incoming core interruption; imported looping playback; stop/phone cleanup. Reports: Temp/RehearBlendTests.txt and Temp/RehearCoreBlendTests.txt.
- Unity PlayMode: 7 passed, including delayed response handling, stale scheduled response cancellation, core+action ordering and six distinct actor start frames.
- Unity EditMode: 51 passed, including speech/boundary/pause classification.
- Server odi/EVC/tests: 100 passed.
- No new live microphone session or APK/device run in this review. Automated pose continuity does not establish subjective naturalness; the revised motion still needs an in-scene visual run.
- Server changes are in the local dev/bora checkout and captured in audience-backchannel.patch; they have NOT been pushed or deployed in this review. The patch base is the existing local server HEAD. Do not apply twice.

Next design — independent judgment clocks (not implemented yet):

1. Timestamp shared speech/content/delivery evidence once. Keep a bounded event buffer; do not run six copies of STT/LLM.
2. Each actor owns next_evaluation_at, last_consumed_evidence_id, state, responsiveness, recent behavior and an independent seeded random stream. Initial phase and interval differ per actor.
3. At an actor's evaluation deadline, consume only unseen evidence since its last judgment. Integrate duration-weighted evidence; never add the same common delta repeatedly because an actor ticks faster. If no new evidence exists, keep the state and listening motion.
4. Select that actor's eligible core/action using its updated state and history. Same judgment may retain the current core; changed judgment can interrupt any presentation action through the existing mixer. Keep explicit Q&A turn reservation.
5. Poll/push due actor decisions separately from the slow STT/LLM request. A single reaction endpoint returning all currently due actors is sufficient; it needs a separate reaction sequence/cursor so polling does not increment the existing audio expected_step.
6. Pair coordination is explicit for rear-seat side conversation. Shared slide events may wake relevant actors with different response probabilities and latencies, without forcing every actor to change pose.
7. Tune intervals against measured fresh-evidence latency. Proposed fast/medium/slow ranges (4–6 / 6–9 / 9–12 seconds) are experimental, not specification requirements. Current periodic audio capture is 8 seconds, so the faster ranges cannot provide fresh content judgments without first improving capture/evaluation latency.

Do not misrepresent client delay as independent evaluation; do not enforce motion dwell times that prevent new evaluations from interrupting current actions.
