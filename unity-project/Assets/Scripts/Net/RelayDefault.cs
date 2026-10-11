using System;
using System.Security.Cryptography;
using System.Text;

// ---------------------------------------------------------------------------------------------------------------------
// Dirección (y token) del relay «oficial» integrada en el juego, CIFRADA (AES-256-CBC + HMAC-SHA256, encrypt-then-MAC).
//
//   * Esto es OFUSCACIÓN, no secreto real: el cliente necesita conocer la dirección para conectar, así que quien desensamble el juego
//     y ejecute este código puede obtenerla. Lo que consigue: que no aparezca en claro en ningún archivo (strings / grep del .dll, .exe,
//     recursos o del repositorio público) ni en la interfaz ni en los logs. La protección real contra extraños es el token del relay.
//   * El texto cifrado y una parte de la clave se generan en la compilación de release (Tools/encrypt_relay.sh -> RelayBlobTool) y se escriben
//     en RelayDefault.Generated.cs, que NO se sube al repositorio (.gitignore). Sin ese archivo el código compila igual
//     (HasBuiltin = false) y el juego usa solo el relay que escriba el usuario / relay.txt.
//   * La clave se deriva en tiempo de ejecución de varias piezas: constantes dispersas aquí (A), un texto troceado y reordenado (B) y la pieza
//     aleatoria por compilación del archivo generado (C), enmascarada y troceada también.
// ---------------------------------------------------------------------------------------------------------------------
public static partial class RelayDefault
{
    // Implementada por RelayDefault.Generated.cs (si existe). chunks/order: texto cifrado troceado y barajado; keyPart/keyOrder: pieza C de la clave.
    static partial void Provide(ref int[][] chunks, ref int[] order, ref int[] keyPart, ref int[] keyOrder);

    // ---- pieza A: constantes dispersas
    private static readonly uint[] S = { 0x9E3779B1u, 0x85EBCA6Bu, 0xC2B2AE35u, 0x27D4EB2Fu, 0x165667B1u, 0xD3A2646Cu, 0xFD7046C5u, 0xB55A4F09u };
    // ---- pieza B: texto troceado y reordenado
    private static readonly string[] W = { "m-r", "ela", "bac", "y-v", "oms", "kro", "-es", "3.k", "cap", "e-s", "alt" };
    private static readonly int[] WO = { 2, 5, 4, 0, 6, 8, 1, 3, 9, 10, 7 };

    // pruebas: simular que no hay relay integrado / inyectar uno propio
    public static bool Suppress;
    private static string injectedAddr, injectedToken;
    private static bool decoded, ok;
    private static string addr = "", token = "";

    public static bool HasBuiltin { get { if (injectedAddr != null) return true; if (Suppress) return false; Decode(); return ok; } }
    public static string Address { get { if (injectedAddr != null) return injectedAddr; if (Suppress) return ""; Decode(); return addr; } }
    public static string Token { get { if (injectedAddr != null) return injectedToken ?? ""; if (Suppress) return ""; Decode(); return token; } }

    public static void TestInject(string a, string t) { injectedAddr = a; injectedToken = t; }

    private static void Decode()
    {
        if (decoded) return;
        decoded = true;
        try
        {
            int[][] chunks = null; int[] order = null, keyPart = null, keyOrder = null;
            Provide(ref chunks, ref order, ref keyPart, ref keyOrder);
            if (chunks == null || order == null || keyPart == null || keyOrder == null) return;
            byte[] blob = Words(Reorder(chunks, order));
            byte[] keyC = Unmask(keyPart, keyOrder);
            string payload = Open(blob, keyC);
            if (payload == null) return;
            int sep = payload.IndexOf('\u001f');
            addr = sep >= 0 ? payload.Substring(0, sep) : payload;
            token = sep >= 0 ? payload.Substring(sep + 1) : "";
            ok = addr.Length > 0;
        }
        catch (Exception) { addr = ""; token = ""; ok = false; }
    }

    // ------------------------------------------------------------------ derivación de claves
    private static byte[] PieceA()
    {
        byte[] a = new byte[32];
        uint x = S[0];
        for (int i = 0; i < a.Length; i++)
        {
            x = x * 1664525u + 1013904223u;
            x ^= S[(i * 5 + 3) & 7] >> (i & 7);
            a[i] = (byte)(x >> (8 * (i & 3)));
        }
        return a;
    }

    private static byte[] PieceB()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < WO.Length; i++) sb.Append(W[WO[i]]);
        using (SHA256 h = SHA256.Create()) return h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static void Keys(byte[] keyC, out byte[] encKey, out byte[] macKey)
    {
        byte[] a = PieceA(), b = PieceB();
        byte[] all = new byte[a.Length + b.Length + keyC.Length];
        Buffer.BlockCopy(a, 0, all, 0, a.Length);
        Buffer.BlockCopy(b, 0, all, a.Length, b.Length);
        Buffer.BlockCopy(keyC, 0, all, a.Length + b.Length, keyC.Length);
        byte[] master;
        using (SHA256 h = SHA256.Create()) master = h.ComputeHash(all);
        using (HMACSHA256 m = new HMACSHA256(master))
        {
            encKey = m.ComputeHash(new byte[] { 0x65, 1 });
            macKey = m.ComputeHash(new byte[] { 0x6d, 2 });
        }
    }

    // ------------------------------------------------------------------ troceado / máscara
    private static int[][] Reorder(int[][] chunks, int[] order)
    {
        int[][] r = new int[order.Length][];
        for (int k = 0; k < order.Length; k++) r[k] = chunks[order[k]];
        return r;
    }

    // palabras (int) -> bytes; la primera palabra es la longitud en bytes
    private static byte[] Words(int[][] logical)
    {
        System.Collections.Generic.List<int> w = new System.Collections.Generic.List<int>();
        foreach (int[] c in logical) w.AddRange(c);
        int len = w[0];
        byte[] b = new byte[len];
        for (int i = 0; i < len; i++) b[i] = (byte)(w[1 + (i >> 2)] >> (24 - 8 * (i & 3)));
        return b;
    }

    private static byte[] Unmask(int[] keyPart, int[] keyOrder)
    {
        byte[] a = PieceA();
        byte[] c = new byte[keyOrder.Length * 4];
        for (int j = 0; j < keyOrder.Length; j++)
        {
            int word = keyPart[keyOrder[j]] ^ (a[(j * 4) % 32] << 24 | a[(j * 4 + 1) % 32] << 16 | a[(j * 4 + 2) % 32] << 8 | a[(j * 4 + 3) % 32]);
            c[j * 4] = (byte)(word >> 24); c[j * 4 + 1] = (byte)(word >> 16); c[j * 4 + 2] = (byte)(word >> 8); c[j * 4 + 3] = (byte)word;
        }
        return c;
    }

    // ------------------------------------------------------------------ AES-256-CBC + HMAC-SHA256: blob = IV(16) | cifrado | etiqueta(32)
    public static string Open(byte[] blob, byte[] keyC)
    {
        try
        {
            if (blob == null || blob.Length < 16 + 16 + 32) return null;
            byte[] encKey, macKey;
            Keys(keyC, out encKey, out macKey);
            int macPos = blob.Length - 32;
            byte[] tag;
            using (HMACSHA256 m = new HMACSHA256(macKey)) tag = m.ComputeHash(blob, 0, macPos);
            int diff = 0;
            for (int i = 0; i < 32; i++) diff |= tag[i] ^ blob[macPos + i];
            if (diff != 0) return null;
            byte[] iv = new byte[16];
            Buffer.BlockCopy(blob, 0, iv, 0, 16);
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = encKey; aes.IV = iv;
                using (ICryptoTransform d = aes.CreateDecryptor())
                {
                    byte[] plain = d.TransformFinalBlock(blob, 16, macPos - 16);
                    return Encoding.UTF8.GetString(plain);
                }
            }
        }
        catch (Exception) { return null; }
    }

#if UNITY_EDITOR
    // Solo editor: cifra address[\u001ftoken] y devuelve el cuerpo del archivo generado (lo usa RelayBlobTool y las pruebas).
    public static byte[] Seal(string payload, byte[] keyC, byte[] iv)
    {
        byte[] encKey, macKey;
        Keys(keyC, out encKey, out macKey);
        byte[] ct;
        using (Aes aes = Aes.Create())
        {
            aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = encKey; aes.IV = iv;
            using (ICryptoTransform e = aes.CreateEncryptor()) { byte[] p = Encoding.UTF8.GetBytes(payload); ct = e.TransformFinalBlock(p, 0, p.Length); }
        }
        byte[] blob = new byte[16 + ct.Length + 32];
        Buffer.BlockCopy(iv, 0, blob, 0, 16);
        Buffer.BlockCopy(ct, 0, blob, 16, ct.Length);
        using (HMACSHA256 m = new HMACSHA256(macKey)) { byte[] tag = m.ComputeHash(blob, 0, 16 + ct.Length); Buffer.BlockCopy(tag, 0, blob, 16 + ct.Length, 32); }
        return blob;
    }

    // pieza C enmascarada con A (para el archivo generado)
    public static int[] MaskKeyWords(byte[] keyC)
    {
        byte[] a = PieceA();
        int n = keyC.Length / 4;
        int[] w = new int[n];
        for (int j = 0; j < n; j++)
        {
            int word = keyC[j * 4] << 24 | keyC[j * 4 + 1] << 16 | keyC[j * 4 + 2] << 8 | keyC[j * 4 + 3];
            w[j] = word ^ (a[(j * 4) % 32] << 24 | a[(j * 4 + 1) % 32] << 16 | a[(j * 4 + 2) % 32] << 8 | a[(j * 4 + 3) % 32]);
        }
        return w;
    }

    // para las pruebas: ¿descifra un blob de pruebas con estas piezas?
    public static string OpenForTest(byte[] blob, byte[] keyC) { return Open(blob, keyC); }
#endif
}
