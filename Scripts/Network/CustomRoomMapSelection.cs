using System;
using Unity.Collections;
using Unity.Netcode;

public struct CustomRoomMapSelection : INetworkSerializable, IEquatable<CustomRoomMapSelection>
{
    private FixedString64Bytes _modeId;
    private FixedString64Bytes _mapSceneName;

    public static CustomRoomMapSelection None => default;

    public string ModeId => _modeId.ToString();
    public string MapSceneName => _mapSceneName.ToString();
    public bool HasSelection => !_modeId.IsEmpty && !_mapSceneName.IsEmpty;

    public CustomRoomMapSelection(string modeId, string mapSceneName)
    {
        _modeId = modeId ?? string.Empty;
        _mapSceneName = mapSceneName ?? string.Empty;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref _modeId);
        serializer.SerializeValue(ref _mapSceneName);
    }

    public bool Equals(CustomRoomMapSelection other)
    {
        return _modeId.Equals(other._modeId) && _mapSceneName.Equals(other._mapSceneName);
    }

    public override bool Equals(object obj)
    {
        return obj is CustomRoomMapSelection other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_modeId, _mapSceneName);
    }
}
// CustomRoomMapSelection은 멀티플레이 세션에서 필요한 상태 전달과 네트워크 수명주기를 관리한다.
// 서버 권한 상태와 클라이언트 표시 상태를 구분하여 중복 실행과 비인가 변경을 방지한다.
