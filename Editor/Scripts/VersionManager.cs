using System.Text.RegularExpressions;
using UnityEditor; // 에디터 스크립팅 필수 네임스페이스
using UnityEngine;

public class VersionManager
{
    // =========================================================
    // 상단 메뉴바에 버튼 생성
    // =========================================================
    [MenuItem("Build Tool/Version/1. Increase Major (대규모 업데이트)")]
    public static void IncreaseMajor() => UpdateVersion(0);

    [MenuItem("Build Tool/Version/2. Increase Minor (기능 추가)")]
    public static void IncreaseMinor() => UpdateVersion(1);

    [MenuItem("Build Tool/Version/3. Increase Patch (버그 수정)")]
    public static void IncreasePatch() => UpdateVersion(2);

    // =========================================================
    // 버전 업데이트 핵심 로직
    // =========================================================
    private static void UpdateVersion(int partToIncrease)
    {
        // 1. 현재 세팅된 버전 가져오기 (예: "1.0.0")
        string currentVersion = PlayerSettings.bundleVersion;
        string[] parts = currentVersion.Split('.');

        int major = parts.Length > 0 ? ExtractNumber(parts[0]) : 0;
        int minor = parts.Length > 1 ? ExtractNumber(parts[1]) : 0;
        int patch = parts.Length > 2 ? ExtractNumber(parts[2]) : 0;

        // 2. 누른 버튼에 따라 숫자 올리고, 하위 숫자는 0으로 초기화
        switch (partToIncrease)
        {
            case 0: // Major (X.0.0)
                major++;
                minor = 0;
                patch = 0;
                break;
            case 1: // Minor (0.X.0)
                minor++;
                patch = 0;
                break;
            case 2: // Patch (0.0.X)
                patch++;
                break;
        }

        // 3. 완성된 버전 문자열 만들기
        string newVersion = $"{major}.{minor}.{patch}";

        // 진짜 바꿀 건지 한 번 더 물어보는 '안전장치 팝업'
        bool isConfirm = EditorUtility.DisplayDialog(
            "버전 업데이트 확인",
            $"정말로 버전을 올리시겠습니까?\n\n[ {currentVersion} ]  ➔  [ {newVersion} ]",
            "Yes", // 확인 버튼
            "No" // 취소 버튼
        );

        // 취소 버튼을 누르거나 팝업을 껐다면 여기서 함수를 강제 종료합니다!
        if (!isConfirm)
        {
            EditorLog.Log("[VersionManager] 버전 업데이트가 취소되었습니다.");
            return;
        }

        // 4. 유니티 Player Settings에 덮어쓰기 (사용자 표시 버전)
        PlayerSettings.bundleVersion = newVersion;

        // 스토어 심사용 정수 빌드 넘버도 알아서 +1 해줍니다!
        PlayerSettings.Android.bundleVersionCode++;

        // iOS는 문자열로 되어 있어서 숫자로 변환 후 +1 해줍니다.
        int iosBuildNum = 0;
        if (int.TryParse(PlayerSettings.iOS.buildNumber, out iosBuildNum))
        {
            PlayerSettings.iOS.buildNumber = (iosBuildNum + 1).ToString();
        }
        else
        {
            PlayerSettings.iOS.buildNumber = "1";
        }

        // 변경 사항을 에디터에 확실히 저장
        AssetDatabase.SaveAssets();

        // 5. 완료 메시지 띄우기 (알림창)
        EditorUtility.DisplayDialog(
            "버전 업데이트 완료!",
            $"기존 버전: {currentVersion}\n"
                + $"새로운 버전: {newVersion}\n\n"
                + $"Android 빌드 넘버: {PlayerSettings.Android.bundleVersionCode}\n"
                + $"iOS 빌드 넘버: {PlayerSettings.iOS.buildNumber}",
            "확인"
        );

        EditorLog.Log(
            $"[VersionManager] {currentVersion} -> {newVersion} 으로 버전이 상승했습니다."
        );
    }

    // 문자열에서 숫자만 찾아내어 int로 바꿔주는 헬퍼 함수
    private static int ExtractNumber(string input)
    {
        // 정규식 \d+ : 연속된 숫자 덩어리를 찾는다.
        // Match : 문자열을 처음부터 읽다가 조건에 맞는 첫 번째 덩어리만 쏙 빼온다.
        Match match = Regex.Match(input, @"\d+");

        if (match.Success && int.TryParse(match.Value, out int result))
        {
            return result;
        }

        return 0; // 숫자가 아예 없다면 0 반환
    }
}
