using AmongUs.QuickChat;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using TMPro;

namespace RandomLanguageMod;

public sealed partial class RandomLanguageBehaviour
{
    [HideFromIl2Cpp]
    private static bool IsProtectedChatOrInput(TMP_Text text)
    {
        for (var node = text.transform; node; node = node.parent)
        {
            string name = node.name;
            if (name.Contains("chat", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("textbox", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return text.GetComponentInParent(Il2CppType.Of<ChatController>(), true) ||
            text.GetComponentInParent(Il2CppType.Of<ChatBubble>(), true) ||
            text.GetComponentInParent(Il2CppType.Of<ChatNotification>(), true) ||
            text.GetComponentInParent(Il2CppType.Of<FreeChatInputField>(), true) ||
            text.GetComponentInParent(Il2CppType.Of<TextBoxTMP>(), true) ||
            text.GetComponentInParent(Il2CppType.Of<TMP_InputField>(), true) ||
            text.GetComponentInParent(Il2CppType.Of<QuickChatMenu>(), true) ||
            text.GetComponentInParent(Il2CppType.Of<QuickChatPreviewField>(), true) ||
            text.GetComponentInParent(Il2CppType.Of<AbstractQuickChatMenuPhrasesPageButton>(), true) ||
            text.GetComponentInParent(Il2CppType.Of<QuickChatMenuLandingPageButton>(), true);
    }
}
