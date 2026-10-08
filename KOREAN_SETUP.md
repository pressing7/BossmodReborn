# pressing7 / BossmodReborn Korean 빌드·배포

이 묶음은 `Korean` 브랜치에 추가할 설정과 앞서 수정한 DMU 파일입니다.
GitHub에는 아직 적용되지 않았으며, 설치 가능한 DLL을 포함한 묶음도 아닙니다.

## 1. 파일 적용

1. `https://github.com/pressing7/BossmodReborn/tree/Korean`을 엽니다.
2. `Add file → Upload files`를 누릅니다.
3. ZIP을 풀고 그 안의 `.github`, `tools`, `BossMod` 폴더와 이 설명 파일을 올립니다.
   바깥의 ZIP/압축 해제 폴더 자체를 올리지 마세요. 기존 폴더 안의 해당 파일들만 추가·교체합니다.
4. `Commit directly to the Korean branch`로 저장합니다.
5. Settings → General → Default branch를 `Korean`으로 바꿉니다.
   수동 실행 버튼을 표시하려면 이 워크플로가 기본 브랜치에 있어야 합니다.
   `main` 브랜치는 삭제되지 않으며 원본 보관용으로 남습니다.
6. Actions에서 워크플로 사용 승인 화면이 나오면 활성화합니다.

## 2. 첫 빌드

Actions → `Korean Build and Release` → `Run workflow` → Branch `Korean`.
처음에는 `Publish ...` 체크를 해제하고 실행합니다.

- 초록색으로 성공하면 실행 화면의 Artifacts → `Korean-package`를 받습니다.
- 바깥 artifact ZIP 안의 `latest.zip`이 실제 플러그인 설치용 ZIP입니다.
- `BossModRebornKR.dll`과 `BossModRebornKR.json`, 필요한 프리셋 등이 들어갑니다.
- 기존 개발용 플러그인을 사용하던 방식으로 테스트할 때 원본 BossMod와 KR 빌드를 동시에 켜지 마세요.
- 새로운 식별자를 쓰므로 기존 플러그인 설정은 자동 이전되지 않습니다. 설정을 다시 하거나 원본 설정을 백업한 후 별도로 옮겨야 합니다.
- DMU 공략 설정은 `Kroxy_Rinon_Melee_Flex`를 선택합니다.

기본 Dalamud 참조 라이브러리 주소는 원본 워크플로와 같은
`https://goatcorp.github.io/dalamud-distrib/latest.zip`입니다.
이 주소는 글로벌 배포본이며 한섭 호환성을 보장하지 않습니다.
이미 정상 빌드에 사용한 한섭 Dalamud 라이브러리 ZIP 주소가 있다면
Settings → Secrets and variables → Actions → Variables → New repository variable에서
`DALAMUD_DISTRIB_URL`로 설정하세요. ZIP 루트에 `Dalamud.dll`이 있어야 합니다.
API가 다르면 소스 호환 작업도 필요합니다. ApiLevel 숫자는 자동으로 바꾸지 않습니다.

프로젝트의 net10.0 타깃은 유지합니다. 원본에 `[with(...)]`가 있기 때문에
C# 15를 지원하는 .NET 11 SDK(미리 보기 포함)도 설치하여 컴파일합니다.
빌드 성공은 게임 내 정상 동작을 보장하지 않습니다.

## 3. 설치 목록에 배포

정상 동작을 확인한 뒤 같은 `Run workflow`에서 `Publish ...`를 체크하고 실행합니다.
배포 단계는 새 ZIP을 GitHub Releases에 올린 다음 `dist` 브랜치의
`pluginmaster.json`을 생성하거나 갱신합니다. `dist`를 미리 만들 필요가 없습니다.

첫 배포가 성공한 뒤 달라무드의 사용자 플러그인 저장소에 다음 주소를 추가합니다.

```text
https://raw.githubusercontent.com/pressing7/BossmodReborn/dist/pluginmaster.json
```

설치 목록에서 **BossMod Reborn KR**을 설치합니다. 기존 원본/개발용 DLL은 비활성화하세요.
이후 새 버전은 달라무드의 업데이트 확인·설치 흐름으로 받습니다.
이름만 한섭 빌드이며, 달라무드 API 호환성은 첫 빌드에서 별도로 맞춰야 합니다.

버전은 `1.(실행번호÷65000).(실행번호 나머지).재시도번호`로 자동 증가합니다.
예: 첫 실행 `1.0.1.1`, 다음 실행 `1.0.2.1`.
원본과 별도 식별자이므로 원본 버전 숫자와 경쟁할 필요가 없습니다.
GitHub의 기본 `GITHUB_TOKEN`을 사용하므로 개인 토큰을 만들 필요가 없습니다.
조직/저장소 정책이 Actions의 쓰기를 막으면 배포 단계는 실패하고 기존 설치 목록을 유지합니다.

## 4. 원본 업데이트 받기

처음에는 원본 병합을 수동으로 하고, 빌드와 배포를 위 워크플로에 맡깁니다.
원본이 바뀌었다고 자동 병합·자동 배포되는 설정은 아닙니다.

GitHub에서 `Korean` 브랜치를 선택하고 `Sync fork → Update branch`를 사용합니다.
자신의 변경사항을 버리는 `Discard commits`는 선택하지 마세요.
충돌이 생기면 원본과 수정본을 비교하여 해결한 다음 다시 빌드합니다.

로컬 Git을 사용한다면 최초 한 번만 원본을 등록합니다.

```bash
git remote add upstream https://github.com/FFXIV-CombatReborn/BossmodReborn.git
```

그다음 업데이트마다 다음을 실행합니다. 작업 중인 수정사항은 먼저 커밋하세요.

```bash
git switch Korean
git fetch upstream
git merge upstream/main
git push origin Korean
```

병합 충돌이 나면 해결하고 병합 커밋을 완료하기 전에는 push하지 않습니다.
`Korean`에 코드를 push하면 자동 빌드가 실행됩니다. 설치 목록 배포는 수동 체크할 때만 합니다.
원본의 기존 `Publish` 워크플로가 아니라 **Korean Build and Release**를 사용하세요.

## 포함 파일과 검증 범위

- `.github/workflows/korean-release.yml`: 빌드와 선택적 배포.
- `tools/korean_release.py`: 빌드 시 임시 식별자 변경, 패키징, 설치 목록 갱신.
- `BossMod/Modules/Dawntrail/Ultimate/DMU/Phase2.cs`: 합의한 조 편성·탑 배정·자리 안내.
- `BossMod/Modules/Dawntrail/Ultimate/DMU/P2Forsaken.cs`: 8탑 종료 후 과거/미래 위치.

기반 커밋: `33456ced5bd96ef37ea62adf98a560f403b241cf`.
빌드 중에만 어셈블리 이름과 패키징 설정을 바꿉니다. 원본 리소스 이름은 보존합니다.
업스트림 프로젝트 구조가 바뀌어 적용 지점을 찾지 못하면 배포를 중단합니다.
워크플로 정적 검사와 배포 스크립트 로컬 검증 대상이며,
GitHub Actions 실제 실행, 전체 플러그인 빌드, 게임 내 실행은 아직 검증하지 않았습니다.
