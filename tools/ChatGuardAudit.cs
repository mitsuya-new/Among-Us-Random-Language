using Mono.Cecil;
using Mono.Cecil.Cil;

using var assembly = AssemblyDefinition.ReadAssembly(args[0]);
var behaviour = assembly.MainModule.Types.Single(t => t.FullName == "RandomLanguageMod.RandomLanguageBehaviour");
MethodDefinition Method(string name) => behaviour.Methods.Single(m => m.Name == name);
IEnumerable<MethodReference> Calls(MethodDefinition method) => method.Body.Instructions
    .Select(i => i.Operand).OfType<MethodReference>();
void Require(bool value, string message) { if (!value) throw new Exception(message); }

var guard = Method("IsProtectedChatOrInput");
var checkedTypes = Calls(guard).OfType<GenericInstanceMethod>().Where(m => m.Name == "Of")
    .SelectMany(m => m.GenericArguments).Select(t => t.FullName).ToHashSet();
foreach (string type in new[] { "ChatController", "ChatBubble", "ChatNotification", "FreeChatInputField", "TextBoxTMP",
    "TMPro.TMP_InputField", "AmongUs.QuickChat.QuickChatMenu", "AmongUs.QuickChat.QuickChatPreviewField",
    "AmongUs.QuickChat.AbstractQuickChatMenuPhrasesPageButton", "AmongUs.QuickChat.QuickChatMenuLandingPageButton" })
    Require(checkedTypes.Contains(type), "Missing protected game component: " + type);
Require(Calls(guard).Where(m => m.Name == "GetComponentInParent").All(m => m is not GenericInstanceMethod),
    "Unsupported generated generic lookup used");
Require(Calls(Method("Discover")).Any(m => m.Name == guard.Name), "Discovery does not protect chat");
Require(Calls(Method("CanTranslate")).Any(m => m.Name == guard.Name), "Classification does not protect chat");

var tick = Method("Tick").Body.Instructions;
var write = tick.Single(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "TMPro.TMP_Text" && m.Name == "set_text");
var check = tick.Last(i => i.Offset < write.Offset && i.Operand is MethodReference m && m.Name == guard.Name);
var restore = tick.Single(i => i.Offset > check.Offset && i.Offset < write.Offset && i.Operand is MethodReference m && m.Name == "RestoreLabel");
Require(tick.Any(i => i.Offset > restore.Offset && i.Offset < write.Offset &&
    i.OpCode.FlowControl == FlowControl.Branch && i.Operand is Instruction target && target.Offset > write.Offset),
    "Protected label path does not skip the text write");

IEnumerable<TypeDefinition> Flatten(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Flatten));
var allCalls = assembly.MainModule.Types.SelectMany(Flatten).SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(Calls).ToArray();
Require(!allCalls.Any(m => m.Name.Contains("SendChat") || m.Name.Contains("SendFreeChat") || m.Name.Contains("SendQuickChat") ||
    m.Name.StartsWith("Rpc") || m.DeclaringType.FullName.StartsWith("Hazel.")), "Chat/network write found");
Require(!allCalls.Any(m => (m.DeclaringType.FullName is "TextBoxTMP" or "FreeChatInputField") &&
    (m.Name.StartsWith("set_") || m.Name is "SetText" or "Submit")), "Input buffer write found");
Console.WriteLine("PASS: game chat/input component coverage, discovery exclusion, guarded text-write branch, non-generic IL2CPP lookup, no chat send/RPC/network/input-buffer mutation.");
