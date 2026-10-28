using System;
using System.Threading.Tasks;
using Modules.SafeSiteApiService.External;
using TMPro;
using UnityEngine;

namespace Modules.SafeSiteApiService.Testbench
{
    /// <summary>Base for one testbench panel: builds its UI and runs actions with a status line.</summary>
    public abstract class TestbenchPanel : MonoBehaviour
    {
        private TextMeshProUGUI _status;
        private int _runId;

        public abstract string Title { get; }

        protected TestbenchState State { get; private set; }

        protected ISafeSiteApiService Service => State.Service;

        protected bool Busy { get; private set; }

        public void Initialize(TestbenchState state, RectTransform content)
        {
            State = state;
            Build(content);
            _status = Ui.Label(content, "", Ui.FontSize, Ui.Muted);
            _status.name = "Status";
            State.Changed += OnStateChanged;
            Service.EnvironmentChanged += _ => OnEnvironmentChanged();
            OnStateChanged();
        }

        protected virtual void OnDestroy()
        {
            if (State != null)
                State.Changed -= OnStateChanged;
        }

        protected abstract void Build(RectTransform content);

        /// <summary>Redraw from stored state. Called after every action in any panel.</summary>
        protected virtual void OnStateChanged() { }

        protected virtual void OnEnvironmentChanged() => OnStateChanged();

        protected void SetStatus(string text, Color color)
        {
            if (_status == null)
                return;
            _status.text = text;
            _status.color = color;
        }

        /// <summary>Runs an action, showing progress, the returned message, or the API error code and message.</summary>
        protected async void Run(string what, Func<Task<string>> action)
        {
            var id = ++_runId;
            Busy = true;
            SetStatus(what + "...", Ui.Muted);
            try
            {
                var message = await action();
                if (id == _runId)
                    SetStatus($"{what}: {message ?? "OK"}", Ui.Good);
            }
            catch (Exception e)
            {
                if (id == _runId)
                    SetStatus($"{what} failed: {TestbenchState.DescribeError(e)}", Ui.Bad);
                if (!(e is ApiError))
                    Debug.LogException(e);
            }
            finally
            {
                Busy = false;
                State.NotifyChanged();
            }
        }

        protected void Run(string what, Func<Task> action) => Run(what, async () =>
        {
            await action();
            return null;
        });

        protected void Run(string what, Func<string> action) => Run(what, () => Task.FromResult(action()));
    }
}
