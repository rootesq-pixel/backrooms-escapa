using UnityEditor;
// v7.8.0: solo las pruebas de edición de V780Checks (rápido):  Unity -batchmode -executeMethod V780Quick.Run
public static class V780Quick
{
    public static void Run()
    {
        SmokeTest.Report r = new SmokeTest.Report("/workspace/logs/v780quick.txt");
        try { V780Checks.Edit(r); } catch (System.Exception e) { r.Fail("excepción " + e); }
        r.Finish();
        EditorApplication.Exit(0);
    }
}
