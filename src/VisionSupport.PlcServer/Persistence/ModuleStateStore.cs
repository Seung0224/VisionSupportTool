using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace VirtualPlcServer.Persistence
{
    /// <summary>
    /// 모듈 목록을 D:\Datas\VisionSupport\VirtualPlcServer\modules\index.json 에 통째로 저장/로드한다.
    /// AppStateStore(프로토콜 타입 하나당 파일 하나)와 달리, 같은 프로토콜의 모듈이 여러 개 있을 수 있으므로
    /// Guid 단위 레코드 목록 하나로 관리한다. 재시작 후 모듈은 항상 Stopped 상태로 복원된다(자동 시작 안 함).
    /// </summary>
    public static class ModuleStateStore
    {
        private static readonly string BaseDirectory = Path.Combine(
            @"D:\Datas", "VisionSupport", "VirtualPlcServer", "modules");

        private static string IndexPath => Path.Combine(BaseDirectory, "index.json");

        /// <summary>모듈 목록을 %AppData%에 두던 시절의 경로. 새 경로에 파일이 없을 때만 대신 읽는다.
        /// 이게 없으면 구버전 exe로 만들어둔 모듈이 새 exe에서 통째로 사라진 것처럼 보인다.</summary>
        private static string LegacyIndexPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VirtualPlcServer", "modules", "index.json");

        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            Converters = { new IPAddressJsonConverter() }
        };

        public static void SaveAll(IEnumerable<ModuleRecord> records)
        {
            Directory.CreateDirectory(BaseDirectory);
            string json = JsonConvert.SerializeObject(records, Formatting.Indented, SerializerSettings);
            File.WriteAllText(IndexPath, json);
        }

        public static List<ModuleRecord> LoadAll()
        {
            string path = IndexPath;
            if (!File.Exists(path) && File.Exists(LegacyIndexPath))
            {
                // 저장(SaveAll)은 항상 새 경로로 가므로, 한 번 실행하면 자연스럽게 옮겨진다.
                path = LegacyIndexPath;
            }

            if (!File.Exists(path))
            {
                return new List<ModuleRecord>();
            }

            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<List<ModuleRecord>>(json, SerializerSettings) ?? new List<ModuleRecord>();
        }
    }
}
