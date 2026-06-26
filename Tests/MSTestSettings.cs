// Repository 테스트들이 PathProvider.AppDataFolder(static)를 공유하므로 클래스 간 순차 실행이 필요합니다.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel, Workers = 1)]
