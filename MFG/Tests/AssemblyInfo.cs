// 테스트마다 MfgPaths.AppDataFolder(static)를 임시 폴더로 바꿔 쓰므로 병렬 실행하면 서로의 DB를 건드린다.
[assembly: DoNotParallelize]
