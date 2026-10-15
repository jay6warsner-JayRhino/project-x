using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
namespace AuraOne;
public static class KeyVault
{
    const string Alias="aura-gemini-key-v1";
    static ISecretKey Key()
    {
        using var store=KeyStore.GetInstance("AndroidKeyStore")!;store.Load(null);
        if(store.ContainsAlias(Alias))return (ISecretKey)store.GetKey(Alias,null)!;
        using var generator=KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes,"AndroidKeyStore")!;
        generator.Init(new KeyGenParameterSpec.Builder(Alias,KeyStorePurpose.Encrypt|KeyStorePurpose.Decrypt).SetBlockModes(KeyProperties.BlockModeGcm).SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone).Build());
        return generator.GenerateKey()!;
    }
    public static void Save(Context context,string value,string slot="gemini")
    {
        using var preferences=context.GetSharedPreferences("private-keys",FileCreationMode.Private)!;
        if(string.IsNullOrWhiteSpace(value)){preferences.Edit()!.Remove(slot)!.Apply();return;}
        using var cipher=Cipher.GetInstance("AES/GCM/NoPadding")!;cipher.Init(CipherMode.EncryptMode,Key());
        var encrypted=cipher.DoFinal(System.Text.Encoding.UTF8.GetBytes(value.Trim()))!;
        preferences.Edit()!.PutString(slot,Convert.ToBase64String(cipher.GetIV()!)+":"+Convert.ToBase64String(encrypted))!.Apply();
    }
    public static string Load(Context context,string slot="gemini")
    {
        try{using var preferences=context.GetSharedPreferences("private-keys",FileCreationMode.Private)!;var value=preferences.GetString(slot,null);if(value==null)return "";var parts=value.Split(':');using var cipher=Cipher.GetInstance("AES/GCM/NoPadding")!;cipher.Init(CipherMode.DecryptMode,Key(),new GCMParameterSpec(128,Convert.FromBase64String(parts[0])));return System.Text.Encoding.UTF8.GetString(cipher.DoFinal(Convert.FromBase64String(parts[1]))!);}catch{return "";}
    }
}
