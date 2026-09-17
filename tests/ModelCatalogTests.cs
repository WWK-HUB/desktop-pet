using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DesktopPet.Chat;

namespace DesktopPet
{
    internal static partial class ChatTests
    {
        private static string ModelList(params string[] ids) { return json.Serialize(new { data = ids.Select(id => new { id = id }).ToArray() }); }
        private static async Task CatalogTests(Atlas atlas, MockServer server)
        {
            string folder = Path.Combine(output, "catalog-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var log = new ChatLog(Path.Combine(folder, "catalog.log"));
            var catalog = new ModelCatalogService(log);
            var settings = new ModelSettings { BaseUrl = server.BaseUrl, ApiKey = FakeKey, TimeoutSeconds = "3" };
            server.Enqueue(200, "{\"data\":[{\"id\":\"z-model\"},{\"id\":\"a-model\"},{\"id\":\"a-model\"},{\"id\":null},{\"id\":\"\"},{\"bad\":true},null]}");
            List<string> ids = await catalog.ListAsync(settings, CancellationToken.None);
            Check(ids.SequenceEqual(new[] { "a-model", "z-model" }), "Models endpoint parses IDs, skips invalid entries, removes duplicates and sorts");
            MockRequest request = server.Requests.Last();
            Check(request.Method == "GET" && request.Path == "/v1/models" && request.Authorization == "Bearer " + FakeKey && request.Body.Count == 0, "Model discovery sends authenticated GET /v1/models without a model ID or chat body");
            foreach (string url in new[] { server.BaseUrl + "/", server.BaseUrl + "/chat/completions/" })
            {
                settings.BaseUrl = url; server.Enqueue(200, ModelList("one")); await catalog.ListAsync(settings, CancellationToken.None);
                Check(server.Requests.Last().Path == "/v1/models", "Discovery accepts trailing slash/full chat endpoint: " + new Uri(url).AbsolutePath);
            }
            settings.BaseUrl = server.BaseUrl.Replace("/v1", "/custom/compatible/v2");
            server.Enqueue(200, ModelList("one")); await catalog.ListAsync(settings, CancellationToken.None);
            Check(server.Requests.Last().Path == "/custom/compatible/v2/models", "Model discovery preserves custom provider API prefix");
            settings.BaseUrl = server.BaseUrl; settings.ApiKey = "";
            server.Enqueue(200, ModelList("local")); await catalog.ListAsync(settings, CancellationToken.None);
            Check(server.Requests.Last().Authorization == "", "Loopback model discovery supports providers without API keys");
            settings.BaseUrl = "https://example.invalid/v1";
            int before = server.Requests.Count;
            await Fails(async () => { await catalog.ListAsync(settings, CancellationToken.None); return ""; }, "config_missing_key");
            Check(server.Requests.Count == before, "Missing remote API key is rejected without sending a request");
            settings.BaseUrl = server.BaseUrl; settings.ApiKey = FakeKey;
            foreach (int status in new[] { 401, 403, 404, 405, 429, 500, 302 })
            {
                server.Enqueue(status, FakeKey);
                await Fails(async () => { await catalog.ListAsync(settings, CancellationToken.None); return ""; }, "models_http_" + status);
            }
            foreach (string invalid in new[] { "not json", "{\"models\":[]}" })
            {
                server.Enqueue(200, invalid);
                await Fails(async () => { await catalog.ListAsync(settings, CancellationToken.None); return ""; }, "models_invalid_response");
            }
            server.Enqueue(200, ModelList());
            await Fails(async () => { await catalog.ListAsync(settings, CancellationToken.None); return ""; }, "models_empty");
            server.Enqueue(200, new string('x', 524300));
            await Fails(async () => { await catalog.ListAsync(settings, CancellationToken.None); return ""; }, "models_response_too_large");
            server.ResetConnections = true;
            try { await Fails(async () => { await catalog.ListAsync(settings, CancellationToken.None); return ""; }, "models_network_error"); }
            finally { server.ResetConnections = false; }
            settings.TimeoutSeconds = "1";
            server.Enqueue(200, ModelList("late"), 1800);
            await Fails(async () => { await catalog.ListAsync(settings, CancellationToken.None); return ""; }, "models_timeout");
            server.Enqueue(200, ModelList("late-body"), 0, 1800);
            await Fails(async () => { await catalog.ListAsync(settings, CancellationToken.None); return ""; }, "models_timeout");
            Check(!File.ReadAllText(log.FilePath).Contains(FakeKey), "Model discovery diagnostics do not contain API key or raw responses");
            int initialRequests = server.Requests.Count;
            using (PetWindow pet = new PetWindow(atlas, false, null, folder))
            {
                pet.Show(); pet.Wander = false; pet.OpenModelSettings();
                ModelSettingsForm form = pet.ModelSettingsWindow;
                form.BaseUrl.Text = server.BaseUrl; form.ApiKey.Text = FakeKey; form.Timeout.Value = 3; form.Model.Text = "";
                await Task.Delay(120);
                Check(form.Model.DropDownStyle == ComboBoxStyle.DropDown && form.Model.Text == "" && form.Model.Items.Count == 0, "Model picker allows both dropdown selection and manual entry");
                Check(server.Requests.Count == initialRequests, "Opening/editing settings does not automatically make network requests");
                server.Enqueue(200, ModelList("z-model", "a-model", "choice-chat-model", "custom/中文模型"), 650);
                form.FetchModels.PerformClick(); await WaitUntil(() => form.IsFetchingModels);
                Check(!form.SaveButton.Enabled && form.FetchModels.Text == "取消获取", "Fetch button exposes pending/cancel state and avoids saving incomplete work");
                int renders = pet.RenderCount; await Task.Delay(180);
                Check(form.IsFetchingModels && pet.RenderCount > renders + 2, "Pet animation continues while model list HTTP request is pending");
                await WaitUntil(() => !form.IsFetchingModels);
                Check(form.Model.Items.Count == 4 && form.Model.Text == "" && form.SaveButton.Enabled, "Fetched models populate dropdown without silently choosing a model");
                Check(!File.Exists(Path.Combine(folder, ModelSettingsStore.FileName)), "Fetching models does not save configuration");
                form.Model.SelectedItem = "choice-chat-model";
                Check(form.Model.Text == "choice-chat-model", "Selecting dropdown model fills its exact model ID");
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                form.Location = new Point(area.Left + 70, area.Top + 60);
                await Task.Delay(100);
                using (Bitmap shot = new Bitmap(form.Width, form.Height))
                {
                    using (Graphics g = Graphics.FromImage(shot)) g.CopyFromScreen(form.Location, Point.Empty, shot.Size);
                    shot.Save(Path.Combine(output, "model-picker-window.png"), ImageFormat.Png);
                }
                // Keep the opened list inside the form so the capture contains only our UI.
                form.Model.DropDownHeight = 102; form.Model.DroppedDown = true; await Task.Delay(100);
                using (Bitmap shot = new Bitmap(form.Width, form.Height))
                {
                    using (Graphics g = Graphics.FromImage(shot)) g.CopyFromScreen(form.Location, Point.Empty, shot.Size);
                    shot.Save(Path.Combine(output, "model-picker-dropdown.png"), ImageFormat.Png);
                }
                form.Model.DroppedDown = false;
                server.Enqueue(200, ModelList("a-model", "another-chat-model")); await form.FetchModelsAsync();
                Check(form.Model.Text == "choice-chat-model", "Refreshing list preserves current model even if provider omits it");
                form.Model.Items.Add("choice-chat-model"); form.Model.SelectedItem = "choice-chat-model";
                form.SaveButton.PerformClick();
                Check(form.IsDisposed && ChatConfig.Load(folder).Model == "choice-chat-model", "Selected dropdown ID saves through existing encrypted settings flow");
                server.Enqueue(200, Answer("selected model works")); await pet.SubmitChatAsync("hello");
                Check((string)server.Requests.Last().Body["model"] == "choice-chat-model", "Actual chat request uses the selected model ID");
                pet.OpenModelSettings(); form = pet.ModelSettingsWindow;
                Check(form.Model.Text == "choice-chat-model", "Saved model is shown on reopen without fetching a list");
                server.Enqueue(404, "not found"); await form.FetchModelsAsync();
                Check(form.Status.ForeColor == Color.Firebrick && form.Status.Text.Contains("手动") && form.SaveButton.Enabled, "Unsupported models endpoint leaves manual input and save available");
                form.Model.Text = "manual-after-error"; form.SaveButton.PerformClick();
                Check(ChatConfig.Load(folder).Model == "manual-after-error", "Manual fallback saves after provider discovery error");
                pet.OpenModelSettings(); form = pet.ModelSettingsWindow;
                server.Enqueue(200, ModelList("old-key-result"), 750);
                int count = server.Requests.Count;
                Task oldRequest = form.FetchModelsAsync(); await WaitUntil(() => server.Requests.Count > count);
                form.ApiKey.Text = FakeKey + "-changed";
                await oldRequest;
                Check(form.Model.Items.Count == 0 && form.Model.Text == "manual-after-error" && form.Status.Text.Contains("重新获取"), "Changing credentials cancels and ignores stale model list without erasing typed model");
                server.Enqueue(200, ModelList("new-key-result")); await form.FetchModelsAsync();
                Check(form.Model.Items.Count == 1 && (string)form.Model.Items[0] == "new-key-result", "Fetching again uses the updated connection details");
                form.BaseUrl.Text = server.BaseUrl + "/chat/completions";
                Check(form.Model.Items.Count == 0, "Changing Base URL invalidates cached provider list");
                server.Enqueue(200, ModelList("cancel-me"), 750);
                count = server.Requests.Count; Task pending = form.FetchModelsAsync(); await WaitUntil(() => server.Requests.Count > count);
                form.FetchModels.PerformClick(); await pending;
                Check(!form.IsFetchingModels && form.Status.Text.Contains("已取消") && form.SaveButton.Enabled, "Cancel-fetch button stops request and restores usable UI");
                server.Enqueue(200, ModelList("too-late"), 750);
                count = server.Requests.Count; Task closing = form.FetchModelsAsync(); await WaitUntil(() => server.Requests.Count > count); form.Close(); await closing;
                Check(form.IsDisposed && pet.Visible, "Closing settings cancels fetch and avoids touching disposed controls");
                pet.Close();
            }
        }
    }
}
