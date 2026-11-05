namespace OpenEQ;
internal static class StartupRegistration {
 internal static string Command(string executable)=>$"\"{executable}\" --start --background";
}
