using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using KeyNest.Core.Security;
using Windows.Security.Credentials;
using Windows.Security.Credentials.UI;
using Windows.Security.Cryptography;

namespace KeyNest.App.Services;

/// <summary>
/// Windows Hello 登録情報。データファイルの鍵を、Hello 鍵の署名から作った鍵で包んだもの。
/// 署名そのものは保存しないので、このファイルだけでは鍵を取り出せない。
/// </summary>
public sealed record HelloEnrollment(string CredentialName, byte[] Challenge, byte[] WrappedKey);

public class HelloException(string message) : Exception(message);

/// <summary>ユーザーが Hello の画面をキャンセルした（マスターパスワードでの解除に切り替える）。</summary>
public sealed class HelloCancelledException() : HelloException("Windows Hello がキャンセルされました。");

/// <summary>登録が使えなくなった（Hello 鍵の削除、マスターパスワードの変更など）。再登録が必要。</summary>
public sealed class HelloEnrollmentInvalidException()
    : HelloException("Windows Hello の登録が無効になっています（マスターパスワードの変更など）。マスターパスワードでロック解除し、設定から登録し直してください。");

/// <summary>
/// Windows Hello によるロック解除。
/// 登録: Hello 鍵（TPM 内の RSA 鍵）でランダムな値に署名 → 署名から包むための鍵を作り、データファイルの鍵を包んで保存。
/// 解除: 同じ値にもう一度署名（Hello の認証が必要）→ 同じ包むための鍵になるので、データファイルの鍵を取り出せる。
/// RSA PKCS#1 v1.5 の署名は同じ入力に対して毎回同じになることを利用している（KeePassXC と同じ方式）。
/// </summary>
public sealed class WindowsHelloService
{
    private const int ChallengeSize = 32;

    /// <summary>この PC で Windows Hello（PIN・顔・指紋）が設定済みか。</summary>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            return await KeyCredentialManager.IsSupportedAsync();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<HelloEnrollment> EnrollAsync(string vaultPath, byte[] vaultKey)
    {
        var name = CredentialName(vaultPath);
        KeyCredential credential;
        using (BringPromptToFront())
        {
            var created = await KeyCredentialManager.RequestCreateAsync(name, KeyCredentialCreationOption.ReplaceExisting);
            ThrowIfFailed(created.Status);
            credential = created.Credential;
        }

        var challenge = RandomNumberGenerator.GetBytes(ChallengeSize);
        var wrappingKey = await DeriveWrappingKeyAsync(credential, challenge);
        try
        {
            return new HelloEnrollment(name, challenge, KeyWrap.Wrap(vaultKey, wrappingKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>Hello で認証して、データファイルの鍵を取り出す。呼び出し側は使用後に消去すること。</summary>
    public async Task<byte[]> UnlockAsync(HelloEnrollment enrollment)
    {
        var opened = await KeyCredentialManager.OpenAsync(enrollment.CredentialName);
        ThrowIfFailed(opened.Status);

        var wrappingKey = await DeriveWrappingKeyAsync(opened.Credential, enrollment.Challenge);
        try
        {
            return KeyWrap.Unwrap(enrollment.WrappedKey, wrappingKey);
        }
        catch (InvalidPasswordException)
        {
            throw new HelloEnrollmentInvalidException(); // Hello 鍵が作り直されて署名が変わった
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>本人確認だけ行う（ロック解除中にトレイから表示するときなど、鍵が不要な場面）。</summary>
    public async Task VerifyUserAsync(string message)
    {
        UserConsentVerificationResult result;
        using (BringPromptToFront())
            result = await UserConsentVerifier.RequestVerificationAsync(message);

        switch (result)
        {
            case UserConsentVerificationResult.Verified:
                return;
            case UserConsentVerificationResult.Canceled:
                throw new HelloCancelledException();
            case UserConsentVerificationResult.DeviceNotPresent:
            case UserConsentVerificationResult.NotConfiguredForUser:
            case UserConsentVerificationResult.DisabledByPolicy:
                throw new HelloException("Windows Hello が使用できません。マスターパスワードを入力してください。");
            default:
                throw new HelloException($"Windows Hello で確認できませんでした（{result}）。");
        }
    }

    public async Task DeleteAsync(string vaultPath)
    {
        try
        {
            await KeyCredentialManager.DeleteAsync(CredentialName(vaultPath));
        }
        catch (Exception)
        {
            // 既に無い場合など。登録ファイルの削除が本体なので無視してよい
        }
    }

    private static async Task<byte[]> DeriveWrappingKeyAsync(KeyCredential credential, byte[] challenge)
    {
        KeyCredentialOperationResult signed;
        using (BringPromptToFront())
            signed = await credential.RequestSignAsync(CryptographicBuffer.CreateFromByteArray(challenge));
        ThrowIfFailed(signed.Status);

        CryptographicBuffer.CopyToByteArray(signed.Result, out var signature);
        try
        {
            return KeyWrap.DeriveWrappingKey(signature);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private static void ThrowIfFailed(KeyCredentialStatus status)
    {
        switch (status)
        {
            case KeyCredentialStatus.Success:
                return;
            case KeyCredentialStatus.UserCanceled:
            case KeyCredentialStatus.UserPrefersPassword:
                throw new HelloCancelledException();
            case KeyCredentialStatus.NotFound:
                throw new HelloEnrollmentInvalidException();
            case KeyCredentialStatus.SecurityDeviceLocked:
                throw new HelloException("セキュリティデバイスがロックされています。しばらくしてから再度お試しください。");
            default:
                throw new HelloException($"Windows Hello でエラーが発生しました（{status}）。");
        }
    }

    /// <summary>データファイルごとに別の Hello 鍵を使う（別ファイルの登録で上書きされないように）。</summary>
    private static string CredentialName(string vaultPath) => "KeyNest_" + VaultId(vaultPath);

    internal static string VaultId(string vaultPath)
    {
        var normalized = Path.GetFullPath(vaultPath).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
    }

    /// <summary>
    /// デスクトップアプリから出した Hello の画面は、後ろに隠れることがある既知の問題への対策。
    /// 表示されるまで短い間隔で探し、見つけたら前面に出す。
    /// </summary>
    private static IDisposable BringPromptToFront()
    {
        const string PromptClassName = "Credential Dialog Xaml Host";
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            var hwnd = NativeMethods.FindWindow(PromptClassName, null);
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(hwnd);
                timer.Stop();
            }
            else if (DateTime.UtcNow > deadline)
            {
                timer.Stop();
            }
        };
        timer.Start();
        return new TimerStopper(timer);
    }

    private sealed class TimerStopper(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}

/// <summary>
/// 登録情報の保存先。Hello 鍵は PC に結び付くので、ポータブルモードでも USB メモリ側ではなく
/// この PC のローカルフォルダ（%LOCALAPPDATA%\KeyNest\hello）に置く。
/// </summary>
public static class HelloEnrollmentStore
{
    private static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyNest", "hello");

    private static string FileFor(string vaultPath) => Path.Combine(Directory, WindowsHelloService.VaultId(vaultPath) + ".json");

    public static HelloEnrollment? Load(string vaultPath)
    {
        try
        {
            var path = FileFor(vaultPath);
            return File.Exists(path) ? JsonSerializer.Deserialize<HelloEnrollment>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(string vaultPath, HelloEnrollment enrollment)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = FileFor(vaultPath);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(enrollment));
        File.Move(temp, path, overwrite: true);
    }

    public static void Delete(string vaultPath)
    {
        try
        {
            File.Delete(FileFor(vaultPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>ロック解除中のデータファイルに対する登録・解除（設定画面から使う）。</summary>
public sealed class VaultHelloController(WindowsHelloService hello, string vaultPath, Core.Storage.VaultSession session)
{
    public bool IsEnrolled => HelloEnrollmentStore.Load(vaultPath) is not null;

    public Task<bool> IsAvailableAsync() => hello.IsAvailableAsync();

    public async Task EnrollAsync()
    {
        var key = session.ExportKey();
        try
        {
            HelloEnrollmentStore.Save(vaultPath, await hello.EnrollAsync(vaultPath, key));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task UnenrollAsync()
    {
        HelloEnrollmentStore.Delete(vaultPath);
        await hello.DeleteAsync(vaultPath);
    }
}
