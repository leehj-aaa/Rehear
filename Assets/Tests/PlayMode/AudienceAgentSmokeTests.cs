using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rehear.Evc.Audience;
using Rehear.Evc.Contracts;
using Rehear.Evc.Presentation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rehear.Evc.Tests
{
    public sealed class AudienceAgentSmokeTests
    {
        [UnityTest]
        public IEnumerator SixConfiguredAgents_AreValidatedAsUnique()
        {
            var root = new GameObject("AudienceTestRoot");
            var coordinator = root.AddComponent<AudienceReactionCoordinator>();
            for (var index = 0; index < 6; index++)
            {
                var agentObject = new GameObject("Agent" + index);
                agentObject.transform.SetParent(root.transform);
                agentObject.AddComponent<AudienceAgent>().Configure("audience_0" + (index + 1), null);
            }

            yield return null;
            Assert.That(coordinator.ValidateSceneAgents(), Is.Empty);
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator CompositeLayers_StartInSameFrameAndDuplicateResponseIsIgnored()
        {
            var fixture = CreateFixture();
            fixture.Clock.Elapsed = 0d;
            fixture.Coordinator.HandleCommands("request-1", new[]
            {
                Command("Face", "face.test", "sync-1", 50),
                Command("Body", "body.test", "sync-1", 50),
                Command("GazeHead", "gaze.test", "sync-1", 50)
            });

            yield return null;

            Assert.That(fixture.Players["Face"].PlayFrames, Has.Count.EqualTo(1));
            Assert.That(fixture.Players["Body"].PlayFrames, Has.Count.EqualTo(1));
            Assert.That(fixture.Players["GazeHead"].PlayFrames, Has.Count.EqualTo(1));
            Assert.That(new HashSet<int>
            {
                fixture.Players["Face"].PlayFrames[0],
                fixture.Players["Body"].PlayFrames[0],
                fixture.Players["GazeHead"].PlayFrames[0]
            }, Has.Count.EqualTo(1));

            fixture.Coordinator.HandleCommands("request-1", new[]
            {
                Command("Face", "face.test", "sync-1", 50),
                Command("Body", "body.test", "sync-1", 50),
                Command("GazeHead", "gaze.test", "sync-1", 50)
            });
            yield return null;
            Assert.That(fixture.Players["Face"].PlayFrames, Has.Count.EqualTo(1));
            Assert.That(fixture.Players["Body"].PlayFrames, Has.Count.EqualTo(1));
            Assert.That(fixture.Players["GazeHead"].PlayFrames, Has.Count.EqualTo(1));

            fixture.Dispose();
        }

        [UnityTest]
        public IEnumerator ScheduledCommand_WaitsWhilePresentationClockIsPaused()
        {
            var fixture = CreateFixture();
            fixture.Clock.Elapsed = 0d;
            fixture.Clock.Paused = true;
            var command = Command("Body", "body.test", "sync-paused", 50);
            command.start_time = 0f;
            fixture.Coordinator.HandleCommands("request-paused", new[] { command });

            yield return null;
            yield return null;
            Assert.That(fixture.Players["Body"].PlayFrames, Is.Empty);

            fixture.Clock.Paused = false;
            yield return null;
            Assert.That(fixture.Players["Body"].PlayFrames, Has.Count.EqualTo(1));

            fixture.Dispose();
        }

        [UnityTest]
        public IEnumerator HigherPriorityWinsSameLayerCollisionAndUnknownActionDoesNotStopOtherLayer()
        {
            var fixture = CreateFixture();
            fixture.Coordinator.HandleCommands("request-priority", new[]
            {
                Command("Body", "body.low", "sync-priority", 50),
                Command("Body", "body.high", "sync-priority", 100),
                Command("Face", "face.unknown", "sync-priority", 100),
                Command("GazeHead", "gaze.test", "sync-priority", 50)
            });

            yield return null;

            Assert.That(fixture.Players["Body"].PlayedActionIds, Is.EqualTo(new[] { "body.high" }));
            Assert.That(fixture.Players["Face"].PlayedActionIds, Is.Empty);
            Assert.That(fixture.Players["GazeHead"].PlayedActionIds, Is.EqualTo(new[] { "gaze.test" }));

            fixture.Dispose();
        }

        private static UnityCommandDto Command(string layer, string actionId, string syncGroup, int priority)
        {
            return new UnityCommandDto
            {
                agent_id = "audience_01",
                start_time = 0f,
                layer = layer,
                action_id = actionId,
                duration = 0.1f,
                sync_group = syncGroup,
                priority = priority,
                blend_mode = "override",
                intensity = 1f
            };
        }

        private static CoordinatorFixture CreateFixture()
        {
            var root = new GameObject("AudienceCoordinatorFixture");
            var registry = ScriptableObject.CreateInstance<AudienceActionRegistry>();
            SetPrivateField(registry, "actions", new[]
            {
                Definition("Face", "face.test"),
                Definition("Body", "body.test"),
                Definition("Body", "body.low"),
                Definition("Body", "body.high"),
                Definition("GazeHead", "gaze.test")
            });

            var players = new Dictionary<string, TrackingActionPlayer>();
            for (var index = 0; index < 6; index++)
            {
                var agentObject = new GameObject("Agent" + index);
                agentObject.transform.SetParent(root.transform);
                if (index == 0)
                {
                    players.Add("Face", AddPlayer(agentObject, "Face"));
                    players.Add("Body", AddPlayer(agentObject, "Body"));
                    players.Add("GazeHead", AddPlayer(agentObject, "GazeHead"));
                }
                agentObject.AddComponent<AudienceAgent>().Configure("audience_0" + (index + 1), registry);
            }

            var coordinator = root.AddComponent<AudienceReactionCoordinator>();
            Assert.That(coordinator.ValidateSceneAgents(), Is.Empty);
            var clock = new FakeClock();
            coordinator.SetClock(clock);
            coordinator.SetServerMode(true);
            return new CoordinatorFixture(root, registry, coordinator, clock, players);
        }

        private static TrackingActionPlayer AddPlayer(GameObject gameObject, string layer)
        {
            var player = gameObject.AddComponent<TrackingActionPlayer>();
            player.Configure(layer);
            return player;
        }

        private static AudienceActionDefinition Definition(string layer, string actionId)
        {
            return new AudienceActionDefinition
            {
                actionId = actionId,
                layer = layer,
                animatorStateName = actionId
            };
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private sealed class FakeClock : IPresentationClock
        {
            public double Elapsed;
            public bool Paused;
            public double ElapsedSeconds => Elapsed;
            public bool IsRunning => true;
            public bool IsPaused => Paused;
        }

        private sealed class TrackingActionPlayer : MonoBehaviour, IActionPlayer
        {
            private string layer;
            public readonly List<int> PlayFrames = new List<int>();
            public readonly List<string> PlayedActionIds = new List<string>();
            public string Layer => layer;

            public void Configure(string value)
            {
                layer = value;
            }

            public bool CanPlay(AudienceActionDefinition action, string blendMode)
            {
                return action != null && action.layer == layer && blendMode == "override";
            }

            public void Play(AudienceActionDefinition action, UnityCommandDto command)
            {
                PlayFrames.Add(Time.frameCount);
                PlayedActionIds.Add(action.actionId);
            }

            public void StopAndRestoreBaseline()
            {
            }
        }

        private sealed class CoordinatorFixture
        {
            private readonly GameObject root;
            private readonly AudienceActionRegistry registry;

            public CoordinatorFixture(
                GameObject root,
                AudienceActionRegistry registry,
                AudienceReactionCoordinator coordinator,
                FakeClock clock,
                Dictionary<string, TrackingActionPlayer> players)
            {
                this.root = root;
                this.registry = registry;
                Coordinator = coordinator;
                Clock = clock;
                Players = players;
            }

            public AudienceReactionCoordinator Coordinator { get; }
            public FakeClock Clock { get; }
            public Dictionary<string, TrackingActionPlayer> Players { get; }

            public void Dispose()
            {
                Object.Destroy(root);
                Object.Destroy(registry);
            }
        }
    }
}
