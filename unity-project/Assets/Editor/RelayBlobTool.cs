using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

// Genera Assets/Scripts/Net/RelayDefault.Generated.cs con el relay oficial CIFRADO (ver RelayDefault.cs).
// El archivo generado NO se sube al repositorio (.gitignore). Uso (compilación de release):
//   RELAY_ADDR=host:puerto [RELAY_TOKEN=...] Unity -batchmode -quit -projectPath . -executeMethod RelayBlobTool.GenerateFromEnv
// (lo envuelve Tools/encrypt_relay.sh). Sin RELAY_ADDR borra el archivo generado => el juego compila sin relay integrado.
public static class RelayBlobTool
{
    public const string OutPath = "Assets/Scripts/Net/RelayDefault.Generated.cs";

    public static void GenerateFromEnv()
    {
        string addr = (Environment.GetEnvironmentVariable("RELAY_ADDR") ?? "").Trim();
        string token = (Environment.GetEnvironmentVariable("RELAY_TOKEN") ?? "").Trim();
        if (addr.Length == 0) { Remove(); Console.WriteLine("[RelayBlobTool] RELAY_ADDR vacío: sin relay integrado."); return; }
        string h; int p;
        if (!NetAddress.TryParse(addr, RelayConfig.DefaultPort, out h, out p)) { Console.WriteLine("[RelayBlobTool] RELAY_ADDR no válida."); EditorApplication.Exit(2); return; }
        Generate(addr, token);
        Console.WriteLine("[RelayBlobTool] generado " + OutPath + " (" + new FileInfo(OutPath).Length + " bytes; token " + (token.Length > 0 ? "sí" : "no") + ")");
    }

    [MenuItem("Backrooms/Relay/Quitar relay integrado")]
    public static void Remove()
    {
        foreach (string f in new[] { OutPath, OutPath + ".meta" }) if (File.Exists(f)) File.Delete(f);
        AssetDatabase.Refresh();
    }

    public static void Generate(string addr, string token)
    {
        byte[] keyC = Rand(32), iv = Rand(16);
        string payload = addr + (token.Length > 0 ? "\u001f" + token : "");
        byte[] blob = RelayDefault.Seal(payload, keyC, iv);
        if (RelayDefault.OpenForTest(blob, keyC) != payload) throw new Exception("la comprobación de ida y vuelta falló");

        // palabras: [longitud, datos...] en trozos de 5, barajados
        int nWords = 1 + (blob.Length + 3) / 4;
        int[] words = new int[nWords];
        words[0] = blob.Length;
        for (int i = 0; i < blob.Length; i++) words[1 + (i >> 2)] |= blob[i] << (24 - 8 * (i & 3));
        const int chunk = 5;
        int nChunks = (nWords + chunk - 1) / chunk;
        int[] perm = Shuffle(nChunks);                   // perm[k] = posición física del trozo lógico k
        int[][] phys = new int[nChunks][];
        for (int k = 0; k < nChunks; k++)
        {
            int len = Math.Min(chunk, nWords - k * chunk);
            int[] c = new int[len];
            Array.Copy(words, k * chunk, c, 0, len);
            phys[perm[k]] = c;
        }
        int[] keyMasked = RelayDefault.MaskKeyWords(keyC);
        int[] keyPerm = Shuffle(keyMasked.Length);
        int[] keyPhys = new int[keyMasked.Length];
        for (int j = 0; j < keyMasked.Length; j++) keyPhys[keyPerm[j]] = keyMasked[j];

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("// GENERADO por RelayBlobTool — NO SUBIR AL REPOSITORIO PÚBLICO (está en .gitignore).");
        sb.AppendLine("public static partial class RelayDefault");
        sb.AppendLine("{");
        sb.AppendLine("    static partial void Provide(ref int[][] chunks, ref int[] order, ref int[] keyPart, ref int[] keyOrder)");
        sb.AppendLine("    {");
        sb.AppendLine("        chunks = new int[][] {");
        for (int i = 0; i < nChunks; i++) sb.AppendLine("            new int[] { " + string.Join(", ", Array.ConvertAll(phys[i], x => x.ToString())) + " },");
        sb.AppendLine("        };");
        sb.AppendLine("        order = new int[] { " + string.Join(", ", Array.ConvertAll(perm, x => x.ToString())) + " };");
        sb.AppendLine("        keyPart = new int[] { " + string.Join(", ", Array.ConvertAll(keyPhys, x => x.ToString())) + " };");
        sb.AppendLine("        keyOrder = new int[] { " + string.Join(", ", Array.ConvertAll(keyPerm, x => x.ToString())) + " };");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        File.WriteAllText(OutPath, sb.ToString());
        AssetDatabase.Refresh();
    }

    private static byte[] Rand(int n) { byte[] b = new byte[n]; using (RandomNumberGenerator r = RandomNumberGenerator.Create()) r.GetBytes(b); return b; }

    private static int[] Shuffle(int n)
    {
        int[] a = new int[n];
        for (int i = 0; i < n; i++) a[i] = i;
        byte[] rnd = Rand(n * 4);
        for (int i = n - 1; i > 0; i--) { int j = (int)(BitConverter.ToUInt32(rnd, i * 4) % (uint)(i + 1)); int t = a[i]; a[i] = a[j]; a[j] = t; }
        return a;
    }
}
