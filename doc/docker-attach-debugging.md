# 后端容器内附加调试指南（VS Code + Docker）

本文描述**如何把 VS Code 的 .NET 调试器附加（attach）到 `docker compose` 容器里的后端进程**，
逐项说明每个配置的作用、每一步的操作方式，以及常见故障的排查方法。

- 适用对象：`travel-map` 仓库的 `src/backend` 下三个 .NET 服务。
- 相关前置阅读：仓库根 `README.md` 的「开发模式：`docker compose watch`」、`AGENTS.md`。
- 配套文件：`.vscode/launch.json`、`.vscode/tasks.json`、`docker-compose.dev.yml`、三个服务的 `Dockerfile.dev`。

---

## 0. 结论速览（TL;DR）

```text
① 容器跑起来（up -d + watch）
② 往要被调试的容器里装一次 vsdbg（重建容器后要重装）
③ VS Code 配置下拉选 「docker: attach <服务名>」
④ F5 → 在弹出的进程列表里选命令行含 bin/Debug/net10.0/<app> 的那条
⑤ 断点变实心 → 触发一次请求 → 命中
```

三个关键点（缺一不可）：

| 关键点 | 不满足会怎样 |
|--------|--------------|
| 容器内存在 `/vsdbg/vsdbg` | 报 `Pipe program exited unexpectedly` / 找不到调试器 |
| `launch.json` 里有 `pipeTransport` | 弹出的进程列表是**宿主机**的进程 |
| 配置里**不写** `processId`，并有 `sourceFileMap` | 要么弹本机列表，要么断点是**空心**（未绑定） |

---

## 1. 原理：为什么不能直接 F5

```
┌─ 宿主机（Windows）──────────────┐        ┌─ 容器 travel-api-dev（Linux）──────────┐
│                                 │        │                                       │
│  VS Code                        │        │  PID 1  dotnet watch --project ...    │
│    └─ C# 扩展（coreclr 调试器） │        │    └─ PID ... dotnet-watch.dll        │
│         │                       │        │         └─ PID ... dotnet run ...     │
│         │  ① 通过 stdio 管道    │        │              └─ PID 565  travel        │
│         └──── docker exec -i ───┼───────►│                     （应用进程）      │
│              ③ 调试协议 (MI)    │        │  ② /vsdbg/vsdbg（容器内调试器）       │
│                                 │        │                                       │
└─────────────────────────────────┘        └───────────────────────────────────────┘
```

- 被调试的进程在**容器里**，宿主机上的 VS Code 看不到它也断不了它。
- 因此需要两件东西：
  1. **一条通往容器的通道**：`docker exec -i <容器>`（`pipeTransport` 负责）；
  2. **容器内的调试器**：`/vsdbg/vsdbg`（需要手工装一次）。
- 调试器读 PDB 得到的是**容器内路径**（构建发生在容器里，例如 `/src/services/...`），
  而源码开在宿主机上（`src/backend/services/...`），所以还需要 `sourceFileMap` 做路径翻译。

> 注意：整个过程只通过 `docker exec` 的 stdio 通信，**不会**在容器或宿主机上开放任何调试端口。

---

## 2. 前置条件

| 项 | 要求 | 校验方式 |
|----|------|----------|
| Docker CLI | 能被 VS Code 扩展宿主进程调用（在 `PATH` 上） | 终端执行 `docker ps` 有输出 |
| 容器已启动 | 用 `docker compose -f docker-compose.dev.yml up -d` 起过 | `docker ps` 能看到三个后端容器 |
| 源码同步 | 建议同时跑 `docker compose -f docker-compose.dev.yml watch` | 改文件后容器内 `dotnet watch` 有重建日志 |
| C# 扩展 | `ms-dotnettools.csharp`（本文依据 **v2.140.9** 的行为编写） | 扩展面板查看版本 |
| .NET SDK | 容器镜像 `mcr.microsoft.com/dotnet/sdk:10.0` | 由 `Dockerfile.dev` 决定 |

容器与配置的对应关系（**改端口/容器名时要同步改 `launch.json`**）：

| 服务 | 项目 | 容器名 | 宿主端口 | 调试配置名 | 目标进程 |
|------|------|--------|----------|------------|----------|
| user-service | `User.Api/user.csproj` | `dotnet-api-dev` | 8080 | `docker: attach user-service` | `bin/Debug/net10.0/user` |
| travel-service | `Travel.Api/travel.csproj` | `travel-api-dev` | 8081 | `docker: attach travel-service` | `bin/Debug/net10.0/travel` |
| identity-service | `Identity.Api/identity.csproj` | `identity-api-dev` | 8090 | `docker: attach identity-service` | `bin/Debug/net10.0/identity` |

---

## 3. 步骤一：在容器里安装 vsdbg（一次性，重建后需重装）

### 3.1 为什么需要

`mcr.microsoft.com/dotnet/sdk:10.0` 镜像**不含**调试器（已实测：`curl`/`wget`/`ps` 都在，`/vsdbg` 不存在）。
`Dockerfile.dev` 的 `ENTRYPOINT` 是 `dotnet watch ...`，也没有安装调试器的步骤，所以必须进容器装一次。

### 3.2 安装命令

```bash
# 单个容器
docker exec travel-api-dev bash -c "curl -sSL https://aka.ms/getvsdbgsh -o /tmp/v.sh && bash /tmp/v.sh -v latest -l /vsdbg"
```

```powershell
# PowerShell：三个容器一起装
foreach ($c in @('dotnet-api-dev','travel-api-dev','identity-api-dev')) {
    Write-Host "==> $c"
    docker exec $c bash -c "curl -sSL https://aka.ms/getvsdbgsh -o /tmp/v.sh && bash /tmp/v.sh -v latest -l /vsdbg && ls /vsdbg"
}
```

### 3.3 验证安装成功

```powershell
foreach ($c in @('dotnet-api-dev','travel-api-dev','identity-api-dev')) {
    Write-Host -NoNewline "$c -> "; docker exec $c ls -l /vsdbg/vsdbg
}
```

期望输出三个 `-rwxrwxrwx 1 root root ... /vsdbg/vsdbg`。

### 3.4 什么时候需要重装

`/vsdbg` **不在任何卷里**，只要容器被重建就会丢。会触发重建的操作：

- `docker compose -f docker-compose.dev.yml up -d --build <服务>`
- `docker compose watch` 命中 `rebuild` 规则（改了 `Dockerfile.dev`、`package.json` 等）
- `docker compose down` 之后再 `up`

判断容器是否被重建：`docker ps` 里的 `STATUS` 会重置（例如从 `Up 15 minutes` 变回 `Up 10 seconds`）。

### 3.5 可选：固化进开发镜像

不想每次重装，可以在三个服务的 `Dockerfile.dev` 里加一行（**只影响 dev 镜像**）：

```dockerfile
RUN curl -sSL https://aka.ms/getvsdbgsh | bash /dev/stdin -v latest -l /vsdbg
```

代价：镜像变大、构建变慢；收益：重建后调试器还在。按需选择。
**生产镜像（`Dockerfile`）绝对不要加这一行。**

---

## 4. 步骤二：`launch.json` 配置

配置文件：`.vscode/launch.json`。与容器调试相关的部分如下。

### 4.1 三条"自动列进程"配置

三者只有 `pipeArgs` 里的容器名不同：

```jsonc
{
    "name": "docker: attach travel-service",
    "type": "coreclr",              // 使用 C# 扩展的 .NET 调试器
    "request": "attach",            // 附加到已在运行的进程（而不是启动新进程）
    // ↓ 这里故意没有 processId：见 4.4
    "pipeTransport": {
        "pipeCwd": "${workspaceFolder}",                 // 在宿主机哪个目录执行下面的管道命令
        "pipeProgram": "docker",                         // 管道程序：docker CLI
        "pipeArgs": ["exec", "-i", "travel-api-dev"],    // -i 保留 stdin，调试协议走 stdio
        "debuggerPath": "/vsdbg/vsdbg",                  // 容器内调试器路径（步骤一装的）
        "quoteArgs": false                               // docker CLI 不期望命令被引号包裹
    },
    "sourceFileMap": {
        "/src": "${workspaceFolder}/src/backend"         // 容器路径 → 宿主路径 的翻译
    }
}
```

### 4.2 每个配置项的作用

| 配置项 | 作用 | 本项目取值与理由 |
|--------|------|------------------|
| `type` | 选哪个调试器 | `coreclr`＝C# 扩展的 .NET 调试器（.NET 5+） |
| `request` | 启动方式 | `attach`：容器里的进程已经在跑，只附加 |
| `pipeTransport.pipeProgram` | 建立到目标机的通道 | `docker`。等价场景：SSH 用 `ssh`、k8s 用 `kubectl exec` |
| `pipeTransport.pipeArgs` | 通道参数 | `exec -i <容器名>`。`-i` 必须有（调试协议走 stdin）；容器名必须与 `container_name` 一致 |
| `pipeTransport.debuggerPath` | 远端调试器位置 | `/vsdbg/vsdbg`，即步骤一安装的位置 |
| `pipeTransport.pipeCwd` | 执行管道命令的工作目录 | `${workspaceFolder}`；容器名是绝对的，这里不影响结果，但便于日志排查 |
| `pipeTransport.quoteArgs` | 是否自动给参数加引号 | **必须 `false`**。docker CLI 的 `exec` 不接受被整体引号包裹的命令行 |
| `sourceFileMap` | PDB 路径 → 宿主源码路径 | `/src` → `${workspaceFolder}/src/backend`。因为 compose 把宿主的 `./src/backend` 同步到容器 `/src`，容器内编译出的 PDB 记录的是 `/src/services/...` |
| `processId` | 要附加的进程 | **不写**（见 4.4）；写了就跳过自动列进程 |

### 4.3 兜底配置：手动指定容器名 + PID

当自动列进程失败时使用，完全不经过选择器：

```jsonc
{
    "name": "docker: attach (手动指定容器+PID)",
    "type": "coreclr",
    "request": "attach",
    "processId": "${input:dockerPid}",                       // 由输入框提供
    "pipeTransport": {
        "pipeCwd": "${workspaceFolder}",
        "pipeProgram": "docker",
        "pipeArgs": ["exec", "-i", "${input:containerName}"], // 容器名也由输入框提供
        "debuggerPath": "/vsdbg/vsdbg",
        "quoteArgs": false
    },
    "sourceFileMap": { "/src": "${workspaceFolder}/src/backend" }
}
```

配套的输入项声明（`launch.json` 顶层，与 `configurations` 平级）：

```jsonc
"inputs": [
    { "id": "containerName", "type": "promptString",
      "description": "容器名", "default": "travel-api-dev" },
    { "id": "dockerPid", "type": "promptString",
      "description": "容器内应用进程 PID（docker exec <容器> ps -eo pid,args）" }
]
```

- `inputs` 是 VS Code 的通用机制：任何 `${input:<id>}` 都会在启动调试时弹输入框。
- 好处：不用为了换个 PID / 容器名去改文件。

### 4.4 为什么这里**不能**写 `processId: "${command:pickRemoteProcess}"`

这是本项目踩过的坑，务必不要改回去：

1. 在本仓库使用的 C# 扩展 v2.140.9 中，`csharp.listProcess` 与 `csharp.listRemoteProcess`
   **已被注册为空实现**（`registerCommand("csharp.listRemoteProcess", () => "")`），
   所以 `${command:pickRemoteProcess}` 会解析成空串。
2. VS Code 的 `${command:...}` 变量在解析时**不会把 launch 配置当参数**传给命令
   （扩展源码里对此留了注释，指向 microsoft/vscode#110889），
   于是受限于命令签名的 `ShowAttachEntries(args, ...)` 拿不到 `pipeTransport`。
3. 结果是配置提供者的判断 `request === "attach" && !processId && !processName`
   走不到"用 pipeTransport 列进程"的分支，退回本地进程列表
   —— 现象是：**弹出的列表全是宿主机进程（带 `.exe`）**，看起来像配置没生效。

**正确做法：留空 `processId`。** C# 扩展的配置提供者会自己接管：

```js
if (request === "attach" && !processId && !processName) {
    if (pipeTransport) ShowAttachEntries(config, ...)  // → 用 pipeTransport 在容器内列进程 ✅
    else               new AttachPicker(...)           // → 列宿主机进程
}
```

同理，`.vscode/launch.json` 里那条本地附加配置（`attach 本地: pick process`）也只是
`{"type":"coreclr","request":"attach"}` —— 没有 `pipeTransport`，自然弹宿主机进程列表，
`dotnet watch run` 在宿主机跑时用它。

---

## 5. 步骤三：附加并选择进程

1. 打开「运行和调试」面板，在配置下拉里选 **`docker: attach <服务名>`**
   （例如调试 travel-service 选 `docker: attach travel-service`）。
   - ⚠️ 不要选 `backend: travel-service (8081)` —— 那是**宿主本地调试**用的配置，
     没有 `pipeTransport`，弹出的必然是宿主机进程列表。
   - 下拉顶部的文字要显示成 `docker: attach ...` 才说明选对了。
2. 按 **F5**。
3. 弹出「选择要附加到的进程」列表 —— **这一次列出的应该是容器内的 Linux 进程**。

容器里的进程树（以 travel-service 为例）：

| 列表条目 | 命令行 | 选？ | 说明 |
|----------|--------|------|------|
| `dotnet  1` | `dotnet watch --project services/travel-history/src/Travel.Api/travel.csproj run ...` | ❌ | 容器入口（`Dockerfile.dev` 的 ENTRYPOINT），附上去不会进业务代码 |
| `dotnet  24` | `/usr/share/dotnet/dotnet .../dotnet-watch.dll ...` | ❌ | watch 的工作进程 |
| `dotnet  538` | `/usr/share/dotnet/dotnet run --no-build --framework net10.0 ...` | ❌ | watch 用来启动应用的壳 |
| `sh` / `ps` | 选择器自己临时起的脚本 | ❌ | 转瞬即逝 |
| **`travel  565`** | **`/src/services/travel-history/src/Travel.Api/bin/Debug/net10.0/travel --urls http://+:8080`** | ✅ | **应用进程（apphost），选它** |

**判定口诀**：命令行里含 `bin/Debug/net10.0/<app>` 的那条才是应用进程；`<app>` 分别是
`user` / `travel` / `identity`。容器里的进程**没有 `.exe` 后缀**，看到 `.exe` 就说明列的是宿主机。

查 PID 的命令（兜底配置要填，或想核对时用）：

```bash
docker exec travel-api-dev ps -eo pid,args
```

```powershell
docker exec travel-api-dev ps -eo pid,args | Select-String "bin/Debug/net10.0"
```

---

## 6. 步骤四：验证调试生效

1. **断点状态**：打开被附加服务的一个源文件（例如
   `src/backend/services/travel-history/src/Travel.Api/Endpoints/TravelEndpoints.cs`），
   在方法体里点一个断点。附加成功后断点应从**空心**变成**实心红点**。
   - 空心 = `sourceFileMap` 没匹配上，或容器的源码快照与宿主不一致。
2. **触发请求**：

   | 方式 | 命令 / 地址 |
   |------|-------------|
   | health | `curl http://localhost:8081/health` |
   | Swagger | http://localhost:8081/swagger |
   | 业务接口 | `curl -H "Authorization: Bearer <token>" "http://localhost:8081/api/travels?page=1&pageSize=10"` |
   | 前端 | http://localhost:4200 （操作页面触发对应接口） |

   例如在 `GET /api/travels` 的 `Results.Ok(await svc.GetPagedAsync(...))` 那一行打断点，
   然后从浏览器进入旅行列表页，即可命中。

3. 命中后可以正常使用：变量面板、监视、调用堆栈、调试控制台求值、单步（F10/F11）、继续（F5）。

---

## 7. 调试要点与限制

### 7.1 可用能力

- **条件断点 / 日志断点**：右键断点 → 编辑条件或改为"日志消息"，避免高频接口反复中断。
- **调试控制台求值**：表达式在**容器内的进程**里执行，因此能看到容器内的真实状态
  （例如容器路径、容器环境变量、连接串），这也是附加调试相对本地调试的价值所在。
- **异常中断**：可在断点面板勾选要中断的异常类型。

### 7.2 重要限制

| 限制 | 原因 | 影响 / 处理 |
|------|------|-------------|
| 改代码会导致调试会话断开 | `docker compose watch` 同步源码 → 容器内 `dotnet watch` 重建并**重启进程（PID 变化）** | 重新 F5 附加一次；或调试期间先停掉 watch |
| 热重载与调试器互相干扰 | 容器 entrypoint 带 `DOTNET_WATCH=1`、delta applier、BrowserRefresh 等热重载环境变量 | 断点调试期间尽量一次只调一个服务，不要在附加状态下频繁热重载 |
| 容器重建后调试器丢失 | `/vsdbg` 不在卷里 | 重跑步骤 3.2 |
| 断点位置与源码不一致 | 容器的源码/PDB 快照落后于宿主 | 让 `docker compose watch` 同步一次，或 `up -d --build <服务>`；也可看容器内文件与宿主 diff |
| 只能调试 Debug 构建 | Release 会优化，断点/变量不可靠 | 开发镜像默认是 `dotnet watch`（Debug），无需处理 |

---

## 8. 故障排查

先在「输出」面板里找到名为 **`remote-attach`** 的通道（C# 扩展在列远端进程时创建），
它记录了实际执行的命令与结果；需要更详细的日志时，在调试配置里加：

```jsonc
"logging": { "engineLogging": true }
```

| 现象 | 原因 | 处理 |
|------|------|------|
| 弹出的进程列表全是宿主机进程（带 `.exe`：`Code.exe`、`wsl.exe`、`com.docker.backend.exe`…） | ① 选错了配置（选了 `backend: ...` 而不是 `docker: attach ...`）；② 配置里写了 `processId` | 换成 `docker: attach <服务名>`；删除 `processId`；见 4.4 |
| 报 `Pipe program '<docker>' exited unexpectedly with code 1` | 容器已退出 / 容器名写错 / `docker` 不在扩展宿主的 `PATH` 上 | `docker ps` 确认容器名与状态；终端里手动跑一遍 `docker exec -i <容器> /vsdbg/vsdbg --help` 验证 |
| 列表里没有 `<app>` 进程 | 容器内应用启动失败（例如配置缺失导致 fail-closed 拒绝启动）、或还在编译 | `docker logs -f <容器>` 看启动日志 |
| 断点是**空心**（未绑定） | `sourceFileMap` 缺失/写错；容器源码快照与宿主不一致 | 确认 `"/src": "${workspaceFolder}/src/backend"`；对比容器内文件 |
| 附加成功但断点在错误的行停下 | PDB 与源码不同步（宿主改了但容器没重新编译） | 等 `docker watch` 同步完成重建后再附加 |
| 附加后几秒钟自动断开 | 期间发生了 `dotnet watch` 重建（你或 watch 触发了编译） | 重新附加；调试期间减少改动 |
| 提示找不到调试器 / `debuggerPath` 相关错误 | `/vsdbg` 不存在 | 重跑步骤 3.2 安装 |
| 想让容器自己带调试器 | 反复重装太烦 | 见 3.5，把安装写进 `Dockerfile.dev` |

---

## 9. 什么时候用"附加容器"，什么时候用"宿主本地调试"

两种方式在 `.vscode/launch.json` 里都有，按目的选择：

| 维度 | 附加容器（`docker: attach *`） | 宿主本地（`backend: *`） |
|------|------------------------------|--------------------------|
| 调试对象 | 容器内构建的那份产物，与运行环境完全一致 | 宿主上 `dotnet build` 的产物 |
| 依赖 | 需装 vsdbg、需管 PID / 重建 | 需让出端口（`docker compose stop <服务>`）、需配 user-secrets |
| 会话稳定性 | 受 `docker compose watch` 重建影响会断开 | 稳定，改代码后重启即可 |
| 适合场景 | 复现"只有在容器里才出现"的问题；核对容器内实际生效的配置/路径 | 日常打断点、快速迭代 |

宿主本地调试的前置（`user-secrets`、端口让位）见仓库根 `README.md` 与 `.vscode/launch.json` 中的注释。

---

## 10. 相关文件索引

| 文件 | 作用 |
|------|------|
| `.vscode/launch.json` | 全部调试配置（本地启动、本地附加、容器附加、兜底附加、`inputs`） |
| `.vscode/tasks.json` | `build: <服务>` 任务，供本地启动配置的 `preLaunchTask` 使用 |
| `docker-compose.dev.yml` | 容器名、端口映射、环境变量、`develop.watch` 同步规则 |
| `src/backend/services/*/Dockerfile.dev` | 开发镜像：`dotnet watch` 入口，工作目录 `/src` |
| `src/backend/shared/Shared.Security/TravelMapJwt.cs` | 令牌校验（`Identity:Issuer` 同时是 OIDC 发现地址） |
| `README.md` | 开发环境启动方式、`docker compose watch` 说明 |
