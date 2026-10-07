using System;
using System.IO;
using Newtonsoft.Json;

namespace VirtualPlcServer.Persistence
{
    /// <summary>모듈 보드를 어떻게 보여줄지(카드/목록). 모듈 목록(index.json)과는 따로 저장한다 -
    /// index.json은 다른 PC로 통째로 복사해 가는 파일이라, 보기 취향까지 따라가면 안 된다.</summary>
    public sealed class BoardSettings
    {
        public bool IsListLayout { get; set; }
    }

    /// <summary>D:\Datas\VisionSupport\VirtualPlcServer\board.json. 읽기/쓰기 실패는 기본값으로 넘어간다 -
    /// 보기 설정 하나 때문에 보드가 안 열리면 안 된다.</summary>
    public static class BoardSettingsStore
    {
        private static readonly string FilePath = Path.Combine(
            @"D:\Datas", "VisionSupport", "VirtualPlcServer", "board.json");

        public static BoardSettings Load()
        {
            try
            {
                return File.Exists(FilePath)
                    ? JsonConvert.DeserializeObject<BoardSettings>(File.ReadAllText(FilePath)) ?? new BoardSettings()
                    : new BoardSettings();
            }
            catch (Exception)
            {
                return new BoardSettings();
            }
        }

        public static void Save(BoardSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch (Exception)
            {
                // 다음에 기본값(카드)으로 열릴 뿐이다.
            }
        }
    }
}
