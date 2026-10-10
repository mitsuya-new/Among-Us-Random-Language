using Il2CppInterop.Runtime.Attributes;
using TMPro;
using UnityEngine;

namespace RandomLanguageMod;

public sealed partial class RandomLanguageBehaviour
{
    [HideFromIl2Cpp]
    private void RefreshInventoryNames()
    {
        var menu = PlayerCustomizationMenu.Instance;
        if (!menu || !menu.isActiveAndEnabled || menu.Tabs == null) return;
        foreach (var button in menu.Tabs)
        {
            if (button == null || !button.Tab || !button.Tab.isActiveAndEnabled) continue;
            var tab = button.Tab;
            TMP_Text? target = menu.itemName;
            string identity;
            Func<string?> readName;
            string? preferred = null;
            if (tab.TryCast<PlayerTab>() is { } colors)
            {
                int color = colors.currentColor;
                if (color < 0 || color >= Palette.ColorNames.Length) return;
                var key = Palette.ColorNames[color];
                identity = "color:" + color;
                preferred = tables.Values.Any(t => t.ContainsKey(key.ToString())) ? key.ToString() : ((int)key).ToString();
                readName = () => DestroyableSingleton<TranslationController>.Instance.GetString(key, Array.Empty<Il2CppSystem.Object>());
            }
            else if (tab.TryCast<SkinsTab>() is { } skins)
            {
                string id = skins.skinId;
                identity = "skin:" + id;
                readName = () => DestroyableSingleton<HatManager>.Instance.GetSkinById(id)?.GetItemName();
            }
            else if (tab.TryCast<PetsTab>() is { } pets)
            {
                string id = pets.petId;
                identity = "pet:" + id;
                readName = () => DestroyableSingleton<HatManager>.Instance.GetPetById(id)?.GetItemName();
            }
            else if (tab.TryCast<HatsTab>() is { } hats)
            {
                var hat = hats.currentHat;
                if (!hat) return;
                identity = "hat:" + hat.ProductId;
                readName = () => hat.GetItemName();
            }
            else if (tab.TryCast<VisorsTab>() is { } visors)
            {
                string id = visors.visorId;
                identity = "visor:" + id;
                readName = () => DestroyableSingleton<HatManager>.Instance.GetVisorById(id)?.GetItemName();
            }
            else if (tab.TryCast<NameplatesTab>() is { } plates)
            {
                string id = plates.plateId;
                identity = "nameplate:" + id;
                readName = () => DestroyableSingleton<HatManager>.Instance.GetNamePlateById(id)?.GetItemName();
            }
            else if (tab.TryCast<CubesTab>() is { } cubes)
            {
                var cube = cubes.currentCube;
                if (!cube) return;
                target = cubes.itemName;
                identity = "cube:" + cube.productId;
                readName = () => cube.GetCosmicubeName();
            }
            else continue;
            if (!target) return;
            int targetId = target.GetInstanceID();
            if (!labels.TryGetValue(targetId, out var label))
            {
                label = new Label { Text = target, Path = ObjectPath(target.transform) };
                labels[targetId] = label;
            }
            bool selectionChanged = label.SelectionIdentity != identity || !label.InventoryTab ||
                label.InventoryTab!.Pointer != tab.Pointer;
            if (selectionChanged)
            {
                if (label.TabTracker) UnityEngine.Object.Destroy(label.TabTracker);
                label.InventoryTab = tab;
                label.TabTracker = tab.gameObject.AddComponent<RevealTracker>();
                label.TabRevealVersion = label.TabTracker.Version;
                label.ItemIdentity = identity;
                label.SelectionIdentity = identity;
                label.InventoryName = true;
                label.ContentRevealed = true;
                string? source = readName();
                label.Original = source ?? target.text ?? "";
                label.LastInput = target.text ?? "";
                label.Request = catalog!.Resolve(label.Original, preferred);
                if (label.Request != null)
                    label.Request = label.Request with { Upper = IsUppercase(target.text ?? "") };
                string category = identity.Split(':')[0];
                if (reportedInventoryTypes.Add(category))
                    Plugin.Logger.LogInfo($"[ITEM NAME] category={category}, source={label.Original}, key={label.Request?.Key ?? "unresolved"}, distinctTranslations={(label.Request == null ? 0 : catalog.DistinctTranslations(label.Request))}, path={label.Path}");
            }
            if (label.TabTracker && label.TabRevealVersion != label.TabTracker!.Version)
            {
                label.TabRevealVersion = label.TabTracker.Version;
                label.ContentRevealed = true;
            }
            return;
        }
    }

    [HideFromIl2Cpp]
    private static bool IsUppercase(string text) => text.Any(c => char.ToUpperInvariant(c) != char.ToLowerInvariant(c)) &&
        text == text.ToUpperInvariant();
}
