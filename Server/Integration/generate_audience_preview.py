"""Generate a local visual-review trace using the actual server scheduler.

No microphone, provider, HTTP session, or private configuration is used.
Usage: python generate_audience_preview.py <sushi-fast directory> <output.json>
"""
import json
import sys
from pathlib import Path
from uuid import uuid4

sys.path.insert(0, str(Path(sys.argv[1]).resolve()))
from odi.EVC.reaction_scheduler import AudienceReactionScheduler, Evidence, ReactionRequest
from odi.EVC.schema import SmartStartOptions, AudienceState, SegmentContext, SpeechMetrics, EventSignals
from odi.EVC.state_engine import initialize_audiences

seed = 42
agents = initialize_audiences(SmartStartOptions(presentation_title="Local animation review", seed=seed), seed)
scheduler = AudienceReactionScheduler(agents, seed, .5, .5)
session = uuid4()
frames = []
for i in range(480):
    now = i * .25
    if i % 32 == 0:
        phase = int(now // 32) % 4
        e, v, c = [( .15, .10, .12), (-.24, -.12, -.18), (-.10, -.08, -.08), (.30, .20, .30)][phase]
        events = EventSignals(slide_reference=.8 if phase == 0 else 0,
            information_dense=.8 if phase == 0 else 0, long_static_posture=.6,
            low_arousal=.7 if phase == 2 else 0)
        context = SegmentContext(client_time_s=now,
            utterance_position="silence_or_pause" if phase == 2 else "during_speech",
            event_signals=events, slide_reference=phase == 0)
        metrics = SpeechMetrics(duration_s=8,word_count=25,speech_rate_wps=3.1,
            pause_count=1,pause_total_s=1,filler_count=0,repeated_word_count=0,
            avg_confidence=.95,vocal_delivery_score=0)
        scheduler.publish(Evidence(i//32+1, AudienceState(E=e,V=v,C=c), context, metrics,
            "Presentation slide example"))
    response = scheduler.tick(session, ReactionRequest(request_id=uuid4(),client_time_s=now))
    if response.audiences or response.commands:
        frames.append(dict(time=now, response=response.model_dump(mode="json")))
result = dict(duration=120, seed=seed,
    audiences=[dict(agent_id=a.agent_id,profile=a.profile.model_dump(mode="json")) for a in agents],
    frames=frames)
Path(sys.argv[2]).write_text(json.dumps(result,ensure_ascii=False),encoding="utf-8")
print(f"Generated {len(frames)} evaluations, {sum(len(f['response']['commands']) for f in frames)} commands")
