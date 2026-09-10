using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace VirtualPlcServer.Persistence
{
    /// <summary>
    /// 모듈 목록을 %AppData%\VirtualPlcServer\modules\index.json 에 통째로 저장/로드한다.
    /// AppStateStore(프로토콜 타입 하나당 파일 하나)와 달리, 같은 프로토콜의 모듈이 여러 개 있을 수 있으므로
    /// Guid 단위 레코드 목록 하나로 관리한다. 재시작 후 모듈은 항상 Stopped 상태로 복원된다(자동 시작 안 함).
    /// </summary>
    public static class ModuleStateStore
    {
        private static readonly string BaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VirtualPlcServer", "modules");

        private static string IndexPath => Path.Combine(BaseDirectory, "index.json");

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
            if (!File.Exists(IndexPath))
            {
                return new List<ModuleRecord>();
            }

            string json = File.ReadAllText(IndexPath);
            return JsonConvert.DeserializeObject<List<ModuleRecord>>(json, SerializerSettings) ?? new List<ModuleRecord>();
        }
    }
}
