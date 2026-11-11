# ScenarioKit

The one component a scenario talks to: **`SafeSiteScenario`**. It reports score and completion to
SafeSite and asks the app to exit or restart. Start time, duration, language and upload are
automatic.

Guide with wiring examples: [docs/SCENARIO_AUTHORING.md § 3](../../../../docs/SCENARIO_AUTHORING.md#3-report-score-and-completion-safesitescenario).

## API

| Method | Effect |
|---|---|
| `Correct()`, `Incorrect()`, `Answer(bool)` | One scored check; running score sent to analytics |
| `SetScore(correct, total)` | Replace the tally |
| `Complete()` | Session counts as completed (idempotent); raises `onCompleted` once |
| `CompleteAndExit()` | Complete, then back to the main menu |
| `Exit()` | Back to the menu, not completed |
| `Restart()` | Start again as a new session |
| `static FindInScene()` | The component in the loaded scenes |

Fields: `expectedTotal` (unanswered checks count as wrong; 0 = total is the answered count),
`onCompleted` (UnityEvent). Read-only: `CorrectCount`, `AnsweredCount`, `IsCompleted`, `ScoreTotal`.

Outside Bootstrap (scenario scene opened alone) calls are logged and ignored, so scenes still run.
Calls while the app is fading or loading are ignored.

## Assemblies

| Folder | Assembly | Notes |
|---|---|---|
| `Runtime/` | `Modules.ScenarioKit` (auto-referenced) | `SafeSiteScenario`. Scenario scripts in Assembly-CSharp see it with `using Modules.ScenarioKit;`. |
| `Editor/` | `Modules.ScenarioKit.Editor` | Custom Inspector: lists every event in the scene wired to the component (with Show), Play Mode buttons to fake a trainee; `AddTo(scene)` used by the Scenarios window's **Add SafeSite Scenario**. |
| `Samples/` | `Modules.ScenarioKit.Samples` (not auto-referenced) | `ScenarioKitExample.unity`, `ExampleScenarioSteps` (Inspector wiring) and `ExampleScenarioCode` (code). Never ships. |

## Depends on

`SessionAnalyticsService.External` (score, completion), `ScenarioLauncher.External` (`IScenarioHost`
for exit and restart). It doesn't know about the API or the menus.
