using System.Collections.Generic;
using System.Linq;
using Modules.SafeSiteApiService.External;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Modules.SafeSiteApiService.Testbench
{
    /// <summary>Admin sign-in and enrollment-code creation, so a headset can enroll without the dashboard.</summary>
    public sealed class AdminPanel : TestbenchPanel
    {
        private TMP_InputField _email;
        private TMP_InputField _password;
        private TMP_InputField _accessToken;
        private Button _devToken;
        private TextMeshProUGUI _signedIn;
        private TMP_Dropdown _clients;
        private TMP_InputField _maxUses;
        private TMP_InputField _expiresInDays;
        private List<AdminClient> _clientList = new List<AdminClient>();
        private ISupabaseAuth _subscribedAuth;

        public override string Title => "Admin helper";

        protected override void Build(RectTransform content)
        {
            Ui.Heading(content, "Sign in");
            _signedIn = Ui.Label(content, "");
            _email = Ui.Input(content, "Email");
            _password = Ui.Input(content, "Password", "", TMP_InputField.ContentType.Password);

            var row = Ui.Row(content);
            Ui.Button(row, "Sign in", () => Run("Sign in", async () =>
            {
                var password = _password.text;
                _password.text = "";
                await Service.Client.Auth.SignInAsync(_email.text.Trim(), password);
                return "signed in as " + Service.Client.Auth.Email;
            }), color: Ui.Accent);
            Ui.Button(row, "Sign out", () => Run("Sign out", () =>
            {
                Service.Client.Auth.SignOut();
                return "refresh token deleted";
            }));
            _devToken = Ui.Button(row, "Use dev token (Local)", () => Run("Dev token", () =>
            {
                Service.Client.Auth.UseLocalDevToken();
                return "using the local dev token";
            }));

            _accessToken = Ui.Input(content, "Access token", "", TMP_InputField.ContentType.Password);
            Ui.Button(Ui.Row(content), "Use access token", () => Run("Access token", () =>
            {
                Service.Client.Auth.UseAccessToken(_accessToken.text);
                _accessToken.text = "";
                return Service.Client.Auth.IsSignedIn ? "token set" : "token cleared";
            }));

            Ui.Heading(content, "Enrollment code");
            var clientsRow = Ui.Row(content);
            Ui.Button(clientsRow, "Load clients", LoadClients);
            _clients = Ui.DropdownField(clientsRow, new[] { "(load clients)" });
            _maxUses = Ui.Input(content, "Max uses", "1", TMP_InputField.ContentType.IntegerNumber);
            _expiresInDays = Ui.Input(content, "Expires in days", "1", TMP_InputField.ContentType.IntegerNumber);
            Ui.Button(Ui.Row(content), "Create enrollment code", CreateCode, color: Ui.Accent);
        }

        private void Start() => RestoreSession();

        protected override void OnEnvironmentChanged()
        {
            _clientList.Clear();
            Ui.SetOptions(_clients, new[] { "(load clients)" });
            RestoreSession();
            base.OnEnvironmentChanged();
        }

        private void RestoreSession()
        {
            var auth = Service.Client.Auth;
            if (_subscribedAuth != auth)
            {
                if (_subscribedAuth != null)
                    _subscribedAuth.Changed -= OnStateChanged;
                auth.Changed += OnStateChanged;
                _subscribedAuth = auth;
            }

            if (!auth.IsSignedIn && !Service.Environment.IsLocal)
            {
                Run("Restore session", async () =>
                    await auth.TryRestoreAsync() ? "signed in as " + auth.Email : "no saved session");
            }
        }

        private void LoadClients() => Run("Load clients", async () =>
        {
            _clientList = await Service.Client.Admin.GetClientsAsync();
            Ui.SetOptions(_clients, _clientList.Count == 0 ? new[] { "(no clients)" } : _clientList.Select(c => $"{c.Name} ({c.Tier}, {c.ActiveHeadsets}/{c.HeadsetLimit} headsets)"));
            return $"{_clientList.Count} clients";
        });

        private void CreateCode() => Run("Create enrollment code", async () =>
        {
            if (_clientList.Count == 0)
                return "load clients first";
            var client = _clientList[Mathf.Clamp(_clients.value, 0, _clientList.Count - 1)];
            int? maxUses = int.TryParse(_maxUses.text, out var m) ? m : (int?)null;
            int? days = int.TryParse(_expiresInDays.text, out var d) ? d : (int?)null;
            var code = await Service.Client.Admin.CreateEnrollmentCodeAsync(client.Id, maxUses, days);
            State.SetEnrollmentCode(code.Code);
            return $"{code.Code} for {client.Name} (max {code.MaxUses}, expires {code.ExpiresAt}); copied to Enroll";
        });

        protected override void OnStateChanged()
        {
            var auth = Service.Client.Auth;
            var local = Service.Environment.IsLocal;
            _devToken.gameObject.SetActive(local);
            _email.transform.parent.gameObject.SetActive(!local);
            _password.transform.parent.gameObject.SetActive(!local);
            _signedIn.text = auth.IsSignedIn
                ? Ui.Colored($"Signed in: {auth.Email ?? "(token)"}", Ui.Good) + (auth.ExpiresAt.HasValue ? $"  expires {auth.ExpiresAt.Value.ToLocalTime():HH:mm:ss}" : "")
                : Ui.Colored("Not signed in", Ui.Muted) + (local ? "  (Local: use the dev token)" : "");
        }

        protected override void OnDestroy()
        {
            if (_subscribedAuth != null)
                _subscribedAuth.Changed -= OnStateChanged;
            base.OnDestroy();
        }
    }
}
