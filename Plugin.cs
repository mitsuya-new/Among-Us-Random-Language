using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using UnityEngine;

namespace RandomLanguageMod;

[BepInPlugin("local.amongus.randomlanguage", "Random Language", "1.1.2")]
public sealed class Plugin : BasePlugin
{
    internal static ConfigEntry<bool> Enabled = null!;
    internal static ConfigEntry<string> Languages = null!;
    internal static ManualLogSource Logger = null!;
    public override void Load()
    {
        Logger = Log;
        Enabled = Config.Bind("General", "Enabled", true, "Randomize each localized UI label when it is shown.");
        Languages = Config.Bind("General", "Languages", "", "Comma-separated SupportedLangs names; empty uses every loaded language. Restart after changing.");
        ClassInjector.RegisterTypeInIl2Cpp<RevealTracker>();
        AddComponent<OfficialModStampBehaviour>();
        AddComponent<RandomLanguageBehaviour>();
        Log.LogInfo("Random Language v1.1.2 loaded. UI and inventory names reroll; chat and input fields are excluded.");
    }
}

public sealed class RevealTracker : MonoBehaviour
{
    public RevealTracker(IntPtr pointer) : base(pointer) { }
    internal int Version;
    public void OnEnable() => Version++;
}

public sealed partial class RandomLanguageBehaviour : MonoBehaviour
{
    public RandomLanguageBehaviour(IntPtr pointer) : base(pointer) { }
    private sealed class Label
    {
        internal TMP_Text Text = null!;
        internal TextTranslatorTMP? Translator;
        internal RevealTracker? Tracker;
        internal string Path = "", Original = "", Applied = "", LastInput = "";
        internal TranslationCatalog.Request? Request;
        internal int? Language;
        internal int RevealVersion = -1;
        internal bool Visible;
        internal string ItemIdentity = "", SelectionIdentity = "";
        internal bool InventoryName, ContentRevealed;
        internal InventoryTab? InventoryTab;
        internal RevealTracker? TabTracker;
        internal int TabRevealVersion = -1;
    }
    private readonly Dictionary<int, Label> labels = new();
    private readonly Dictionary<string, (int Language, string Text)> previousLanguages = new();
    private readonly HashSet<string> reportedInventoryTypes = new();
    private readonly Dictionary<int, Dictionary<string, string>> tables = new();
    private readonly List<TMP_FontAsset> addedFonts = new();
    private readonly System.Random random = new();
    private TranslationCatalog? catalog;
    private int sourceLanguage = -1;
    private IntPtr controllerPointer;
    private float nextDiscovery, nextInitialization, nextSummary;
    private int translated, rerolled;
    private int mostProtectedChatTexts;
    private bool wasEnabled, warned;

    public void LateUpdate()
    {
        try { Tick(); }
        catch (Exception ex)
        {
            if (!warned) { warned = true; Plugin.Logger.LogError("Random Language UI scan failed: " + ex); }
        }
    }

    [HideFromIl2Cpp]
    private void Tick()
    {
        if (!Plugin.Enabled.Value)
        {
            if (wasEnabled) Restore();
            wasEnabled = false;
            return;
        }
        wasEnabled = true;
        if (Time.unscaledTime >= nextInitialization)
        {
            nextInitialization = Time.unscaledTime + 1f;
            Initialize();
        }
        if (catalog == null) return;
        if (Time.unscaledTime >= nextDiscovery)
        {
            nextDiscovery = Time.unscaledTime + 0.25f;
            Discover();
        }
        RefreshInventoryNames();
        var protectedIds = new List<int>();
        foreach (var label in labels.Values)
        {
            if (!label.Text) continue;
            bool visible = label.Text.isActiveAndEnabled && label.Text.gameObject.activeInHierarchy;
            if (!visible) { label.Visible = false; continue; }
            string current = label.Text.text ?? "";
            bool reveal = !label.Visible || label.ContentRevealed ||
                (label.Tracker && label.RevealVersion != label.Tracker!.Version);
            label.ContentRevealed = false;
            label.Visible = true;
            if (label.Tracker) label.RevealVersion = label.Tracker!.Version;
            if (label.InventoryName && current != label.Applied && current != label.LastInput)
            {
                label.LastInput = current;
                label.Original = current;
                var request = catalog.Resolve(current);
                if (request?.Key != label.Request?.Key)
                {
                    reveal = true;
                    label.ItemIdentity = "preview:" + request?.Key;
                }
                label.Request = request;
            }
            if (!label.InventoryName && current != label.Applied && (current != label.LastInput || label.Request == null))
            {
                label.Original = current;
                if (current != label.LastInput)
                {
                    label.LastInput = current;
                    string? preferred = null;
                    bool upper = false;
                    if (label.Translator)
                    {
                        string named = label.Translator!.TargetText.ToString();
                        string numbered = ((int)label.Translator.TargetText).ToString();
                        preferred = tables.Values.Any(t => t.ContainsKey(named)) ? named : numbered;
                        upper = label.Translator.ToUpper;
                    }
                    var request = catalog.Resolve(current, preferred, upper);
                    if (request?.Key != label.Request?.Key) reveal = true;
                    label.Request = request;
                }
            }
            if (label.Request == null) continue;
            if (!label.Tracker)
            {
                label.Tracker = label.Text.gameObject.AddComponent<RevealTracker>();
                label.RevealVersion = label.Tracker.Version;
            }
            if (reveal || label.Language == null)
            {
                string identity = label.Path + "|" + (label.InventoryName ? label.ItemIdentity : label.Request.Key);
                bool seen = previousLanguages.TryGetValue(identity, out var old);
                int? previous = seen ? old.Language : sourceLanguage;
                var choice = catalog.Choose(label.Request, previous, random, seen ? old.Text : label.Original);
                if (choice == null) continue;
                label.Language = choice.Value.Language;
                if (seen && previous != label.Language) rerolled++;
                if (previousLanguages.Count >= 4096) previousLanguages.Clear();
                previousLanguages[identity] = choice.Value;
            }
            string? desired = catalog.Render(label.Request, label.Language.Value);
            if (desired == null) continue;
            if (IsProtectedChatOrInput(label.Text))
            {
                RestoreLabel(label);
                protectedIds.Add(label.Text.GetInstanceID());
                continue;
            }
            if (current != desired)
            {
                label.Text.text = desired;
                translated++;
            }
            label.Applied = desired;
        }
        foreach (int id in protectedIds) labels.Remove(id);
        if (Time.unscaledTime >= nextSummary)
        {
            nextSummary = Time.unscaledTime + 10f;
            if (translated > 0 || rerolled > 0)
            {
                var counts = labels.Values.Where(l => l.Visible && l.Language != null)
                    .GroupBy(l => (SupportedLangs)l.Language!.Value).Select(g => $"{g.Key}:{g.Count()}");
                Plugin.Logger.LogInfo($"[UI SUMMARY] writes={translated}, redisplayChanges={rerolled}, visible={string.Join(", ", counts)}");
                translated = rerolled = 0;
            }
        }
    }

    [HideFromIl2Cpp]
    private void Initialize()
    {
        var controller = DestroyableSingleton<TranslationController>.Instance;
        if (!controller || controller.currentLanguage == null || controller.Languages == null || controller.Languages.Count < 2) return;
        int current = (int)controller.currentLanguage.languageID;
        if (catalog != null && controller.Pointer == controllerPointer && sourceLanguage == current) return;
        Restore();
        controllerPointer = controller.Pointer;
        sourceLanguage = current;
        tables.Clear();
        var requested = Plugin.Languages.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in controller.Languages)
        {
            if (entry.Value == null || !entry.Value.Data) continue;
            var unit = new LanguageUnit(entry.Value);
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in unit.AllStrings) strings[pair.Key] = pair.Value;
            if (strings.Count == 0) continue;
            tables[(int)entry.Key] = strings;
        }
        catalog = new TranslationCatalog(tables, current);
        if (requested.Length > 0)
        {
            var allowed = tables.Where(p => requested.Any(n => string.Equals(n, ((SupportedLangs)p.Key).ToString(), StringComparison.OrdinalIgnoreCase)))
                .ToDictionary(p => p.Key, p => p.Value);
            catalog = new TranslationCatalog(tables, current, allowed.Keys.ToArray());
        }
        Plugin.Logger.LogInfo($"[CATALOG] loaded={tables.Count}, source={(SupportedLangs)current}, keys={tables.Values.Sum(t => t.Count)}");
        AddFontFallbacks();
        nextDiscovery = 0;
    }

    [HideFromIl2Cpp]
    private void AddFontFallbacks()
    {
        var fallback = TMP_Settings.fallbackFontAssets;
        if (fallback == null) return;
        foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (!font || fallback.Contains(font)) continue;
            fallback.Add(font);
            addedFonts.Add(font);
        }
        Plugin.Logger.LogInfo("[FONTS] added loaded fallbacks: " + string.Join(", ", addedFonts.Select(f => f.name)));
    }

    [HideFromIl2Cpp]
    private void Discover()
    {
        int protectedChatTexts = 0;
        foreach (int id in labels.Where(p => !p.Value.Text).Select(p => p.Key).ToArray()) labels.Remove(id);
        foreach (var text in UnityEngine.Object.FindObjectsOfType<TMP_Text>(true))
        {
            int id = text.GetInstanceID();
            if (IsProtectedChatOrInput(text)) { protectedChatTexts++; continue; }
            if (labels.ContainsKey(id) || !CanTranslate(text)) continue;
            var translator = text.GetComponent<TextTranslatorTMP>();
            labels[id] = new Label { Text = text, Translator = translator, Path = ObjectPath(text.transform) };
        }
        if (protectedChatTexts > mostProtectedChatTexts)
        {
            mostProtectedChatTexts = protectedChatTexts;
            Plugin.Logger.LogInfo($"[CHAT GUARD] protectedTextCount={protectedChatTexts}; chat, quick-chat previews and input fields are excluded.");
        }
    }

    [HideFromIl2Cpp]
    private static bool CanTranslate(TMP_Text text)
    {
        if (IsProtectedChatOrInput(text)) return false;
        for (var node = text.transform; node; node = node.parent)
        {
            string name = node.name.ToLowerInvariant();
            if (name.Contains("chatbubble") || name.Contains("playername") || name.Contains("nameplate") ||
                name.Contains("textbox") || name.Contains("unityexplorer") || name.Contains("universelib")) return false;
        }
        return true;
    }

    [HideFromIl2Cpp]
    private static string ObjectPath(Transform node)
    {
        var parts = new List<string>();
        for (int i = 0; node && i < 32; i++, node = node.parent) parts.Add(node.name + "[" + node.GetSiblingIndex() + "]");
        parts.Reverse();
        return string.Join("/", parts);
    }

    [HideFromIl2Cpp]
    private void Restore()
    {
        foreach (var label in labels.Values) RestoreLabel(label);
        labels.Clear();
        var fallback = TMP_Settings.fallbackFontAssets;
        if (fallback != null) foreach (var font in addedFonts) if (font) fallback.Remove(font);
        addedFonts.Clear();
    }

    [HideFromIl2Cpp]
    private static void RestoreLabel(Label label)
    {
        if (label.Text && label.Text.text == label.Applied) label.Text.text = label.Original;
        if (label.Tracker) UnityEngine.Object.Destroy(label.Tracker);
        if (label.TabTracker) UnityEngine.Object.Destroy(label.TabTracker);
    }

    public void OnDestroy() => Restore();
}
