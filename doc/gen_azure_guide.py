# -*- coding: utf-8 -*-
"""
生成《TravelMap Azure 部署配置完全指南》Word 文档。
仅使用 Python 标准库（zipfile + 手写 OOXML），无需安装 python-docx。
运行：python gen_azure_guide.py
输出：doc/TravelMap-Azure部署配置指南.docx
"""
import zipfile
import os

# ---------------------------------------------------------------- 工具函数

def esc(s):
    return (s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;"))


def run(text, bold=False, italic=False, color=None, font=None, size=None):
    rpr = []
    if bold:
        rpr.append("<w:b/>")
    if italic:
        rpr.append("<w:i/>")
    if color:
        rpr.append('<w:color w:val="%s"/>' % color)
    if font:
        rpr.append('<w:rFonts w:ascii="%s" w:hAnsi="%s" w:eastAsia="%s"/>' % (font, font, font))
    if size:
        rpr.append('<w:sz w:val="%d"/>' % size)
        rpr.append('<w:szCs w:val="%d"/>' % size)
    rpr_xml = "<w:rPr>%s</w:rPr>" % "".join(rpr) if rpr else ""
    return '<w:r>%s<w:t xml:space="preserve">%s</w:t></w:r>' % (rpr_xml, esc(text))


def para(runs, style=None, spacing_after=120, indent=None, shade=None):
    """runs 可以是字符串或 run xml 列表"""
    ppr = []
    if style:
        ppr.append('<w:pStyle w:val="%s"/>' % style)
    ppr.append('<w:spacing w:after="%d"/>' % spacing_after)
    if indent:
        ppr.append('<w:ind w:left="%d"/>' % indent)
    if shade:
        ppr.append('<w:shd w:val="clear" w:color="auto" w:fill="%s"/>' % shade)
    ppr_xml = "<w:pPr>%s</w:pPr>" % "".join(ppr) if ppr else ""
    if isinstance(runs, str):
        runs = [run(runs)]
    return "<w:p>%s%s</w:p>" % (ppr_xml, "".join(runs))


def heading(text, level=1):
    return para(text, style="Heading%d" % level, spacing_after=200)


def code_block(lines):
    if isinstance(lines, str):
        lines = lines.split("\n")
    out = []
    for ln in lines:
        out.append(para(esc(ln), style="CodeBlock", spacing_after=0, shade="F2F2F2"))
    return "".join(out)


def bullet(text_runs, level=0):
    if isinstance(text_runs, str):
        text_runs = run(text_runs)
    numpr = ""
    if level == 0:
        numpr = '<w:pPr><w:pStyle w:val="ListBullet"/><w:spacing w:after="60"/></w:pPr>'
    else:
        numpr = '<w:pPr><w:pStyle w:val="ListBullet2"/><w:spacing w:after="60"/></w:pPr>'
    return "<w:p>%s%s</w:p>" % (numpr, text_runs)


def table(headers, rows, col_widths=None, header_bold=True, font_size=None):
    n = len(headers)
    if col_widths is None:
        col_widths = [10000 // n] * n
    grid = "".join('<w:gridCol w:w="%d"/>' % w for w in col_widths)

    def cell(text_runs, bold=False):
        if isinstance(text_runs, str):
            text_runs = run(text_runs, bold=bold, size=font_size)
        # 支持多行（\n 拆成多个段落）
        tc_inner = []
        if isinstance(text_runs, str) or (isinstance(text_runs, list) and len(text_runs) == 1 and isinstance(text_runs[0], str)):
            txt = text_runs if isinstance(text_runs, str) else text_runs[0]
            for i, ln in enumerate(txt.split("\n")):
                r = run(ln, bold=bold, size=font_size)
                tc_inner.append('<w:p><w:pPr><w:spacing w:after="0"/></w:pPr>%s</w:p>' % r)
        else:
            tc_inner.append('<w:p><w:pPr><w:spacing w:after="0"/></w:pPr>%s</w:p>' % text_runs)
        return '<w:tc><w:tcPr><w:tcW w:w="%d" w:type="dxa"/><w:vAlign w:val="center"/></w:tcPr>%s</w:tc>' % (0, "".join(tc_inner))

    rows_xml = []
    for r_i, row in enumerate(rows):
        cells = []
        for c_i, val in enumerate(row):
            bold = header_bold and r_i == 0
            cells.append(cell(val, bold=bold))
        tbl_header = '<w:trPr><w:tblHeader/></w:trPr>' if r_i == 0 else ""
        rows_xml.append("<w:tr>%s%s</w:tr>" % (tbl_header, "".join(cells)))
    return (
        '<w:tbl><w:tblPr><w:tblStyle w:val="TableGrid"/><w:tblW w:w="0" w:type="auto"/></w:tblPr>'
        "<w:tblGrid>%s</w:tblGrid>%s</w:tbl>" % (grid, "".join(rows_xml))
    )


def spacer():
    return para("", spacing_after=120)


# ---------------------------------------------------------------- 文档内容

BODY = []

BODY.append(heading("TravelMap Azure 部署配置完全指南", 1))
BODY.append(para(run("—— 从租户、订阅、资源组到资源的全链路配置手册（含问题排查）", bold=True, color="1F4E79"), spacing_after=100))
BODY.append(para(run("适用范围：本项目（GitHub 组织 walt-xie-hub / TravelHistoryMap 仓库）通过 GitHub Actions 将三个容器镜像部署到 Azure Container Apps 的全部 Azure 侧配置。", italic=True), spacing_after=200))

# ---------------- 第一章
BODY.append(heading("第一章 总体架构与部署链路", 1))
BODY.append(heading("1.1 应用部署架构", 2))
BODY.append(code_block([
    "浏览器 ──► gateway（nginx 网关，唯一 external 公网入口）",
    "              ├── /api/*  ──► user-service（internal，.NET WebAPI :8080）",
    "              └── /*      ──► client（internal，nginx 静态站 :80）",
]))
BODY.append(para("三个容器应用全部运行在 Azure Container Apps 环境 cae-travelmap 中；数据库使用 Azure PostgreSQL Flexible Server（B1ms 免费层）。只有 gateway 对外暴露，其余两个仅内网可达，减少攻击面。", spacing_after=200))

BODY.append(heading("1.2 CI/CD 部署链路", 2))
BODY.append(code_block([
    "push main / 手动触发",
    "   │",
    "   ▼",
    "job ① docker-build：checkout → buildx → 登录 GHCR → 构建并推送 3 个镜像（ghcr.io/walt-xie-hub/*）",
    "   │ （容器内执行 dotnet test / ng test，测试失败即构建失败）",
    "   ▼  needs: docker-build（仅 push main / 手动触发，PR 跳过）",
    "job ② deploy：azure/login（OIDC 无密码）→ container-apps-deploy × 3，更新 3 个应用的镜像",
]))

BODY.append(heading("1.3 涉及组件总览（自顶向下）", 2))
BODY.append(table(
    ["层级", "组件", "在本文第几章", "一句话作用"],
    [
        ["租户（Microsoft Entra ID）", "App Registration + Federated Credentials + 服务主体", "第二章", "让 GitHub Actions 能无密码登录 Azure"],
        ["订阅", "资源提供程序注册 + 角色分配（Contributor）", "第三章", "开通服务能力 + 授权 CI 操作权限"],
        ["资源组", "rg-travelmap", "第四章", "把本次部署的所有资源归到一个逻辑分组"],
        ["资源", "Container Apps 环境 / 3 个容器应用 / PostgreSQL", "第五章", "实际运行的服务"],
        ["GitHub", "仓库 Secrets + ci.yml workflow", "第六章", "触发构建、推送镜像、执行部署"],
    ],
    col_widths=[2100, 3600, 1300, 3000],
))

# ---------------- 第二章
BODY.append(heading("第二章 租户（Tenant / Microsoft Entra ID）", 1))
BODY.append(heading("2.1 租户是什么", 2))
BODY.append(para("Azure 租户（Tenant）就是你的 Microsoft Entra ID（旧称 Azure AD）目录，是所有身份与权限的顶层容器。本项目的核心诉求是：让 GitHub Actions 在无密码（OIDC）情况下登录 Azure 执行部署，这件事就是由租户里的三个对象协同完成的："))
BODY.append(bullet(run("App Registration（应用注册）：在租户里登记一个应用身份，作为 GitHub Actions 的\"登录账号\"。", bold=True)))
BODY.append(bullet(run("Federated Credentials（联合凭据）：声明\"我信任 GitHub 这个来源\"，并限定只信任本仓库的 main 分支。", bold=True)))
BODY.append(bullet(run("Enterprise Application / Service Principal（服务主体）：应用注册生效后自动产生的实例，真正在 Azure 里执行操作的对象，需要给它授权（见第三章）。", bold=True)))

BODY.append(heading("2.2 需要配置什么、为什么配置", 2))
BODY.append(table(
    ["配置项", "为什么需要", "关键值"],
    [
        ["App Registration", "GitHub Actions 用它的 Application (client) ID 请求登录令牌", "Client ID = 仓库 secret 中 AZUREAPPSERVICE_CLIENTID_... 的值（形如 07921b08-...）"],
        ["Federated Credential", "Azure 只接受它\"声明信任\"的 GitHub 来源；声明不对就登录失败", "subject 必须精确匹配：repo:walt-xie-hub@317605909/TravelHistoryMap:ref:refs/heads/main"],
        ["服务主体授权", "登录成功后要能操作资源，否则报\"没有权限查看订阅\"", "给服务主体分配 Contributor 角色（见 3.4）"],
    ],
    col_widths=[2100, 4200, 3700],
))

BODY.append(heading("2.3 怎么配置", 2))
BODY.append(para(run("2.3.1 创建/确认 App Registration（门户）", bold=True), spacing_after=80))
BODY.append(bullet("门户搜索 Microsoft Entra ID → App registrations → 新建注册"))
BODY.append(bullet("记录下 Application (client) ID 与 Directory (tenant) ID，它们对应仓库的两个 secret"))
BODY.append(bullet("该应用注册通常由首次创建 Azure App Service / 部署向导时自动生成，仓库里的三个 AZUREAPPSERVICE_* secret 即来源"))

BODY.append(para(run("2.3.2 新增 Federated Credential（推荐 CLI，Cloud Shell）", bold=True), spacing_after=80))
BODY.append(code_block([
    'az ad app federated-credential list --id "<CLIENT_ID>" --query "[].{name:name, subject:subject}" -o table',
    'az ad app federated-credential create --id "<CLIENT_ID>" --parameters \'{',
    '  "name": "github-walt-xie-hub-travelhistorymap-main",',
    '  "issuer": "https://token.actions.githubusercontent.com",',
    '  "subject": "repo:walt-xie-hub@317605909/TravelHistoryMap:ref:refs/heads/main",',
    '  "audiences": ["api://AzureADTokenExchange"]',
    '}\'',
]))

BODY.append(para(run("2.3.3 门户手动路径（等价）", bold=True), spacing_after=80))
BODY.append(bullet("Microsoft Entra ID → App registrations → 选中应用 → Certificates & secrets → Federated credentials"))
BODY.append(bullet("场景：GitHub Actions deploying Azure resources"))
BODY.append(bullet("Organization = walt-xie-hub，Repository = TravelHistoryMap，Branch = main"))
BODY.append(bullet("保存后确认\"主题标识符\"为 repo:walt-xie-hub@317605909/TravelHistoryMap:ref:refs/heads/main"))

BODY.append(heading("2.4 本项目踩过的坑（必读）", 2))
BODY.append(table(
    ["现象", "根因", "解决办法"],
    [
        ["Login to Azure 失败，错误提到 token 里的仓库路径是 walt-xie-hub/TravelHistoryMap，但 Azure 侧只信任旧仓库", "仓库从个人迁移到组织后，Federated Credential 的 subject 还停留在旧仓库", "把 subject 更新为 repo:walt-xie-hub@317605909/TravelHistoryMap:ref:refs/heads/main"],
        ["OIDC 报 subject 不匹配，提示 refs/heads/master", "Azure 里配的分支是 master，而仓库默认分支是 main", "把 Branch/主题标识符改为 main"],
    ],
    col_widths=[3400, 3300, 3300],
))
BODY.append(para(run("注意：一个应用可配多条 Federated Credential；若未来支持 workflow_dispatch 或 PR 部署，可再加 subject：repo:walt-xie-hub/TravelHistoryMap:pull_request。", italic=True), spacing_after=200))

# ---------------- 第三章
BODY.append(heading("第三章 订阅（Subscription）", 1))
BODY.append(heading("3.1 订阅是什么", 2))
BODY.append(para("订阅是资源的计费与权限边界容器。资源组和资源都必须挂在某个订阅下。本项目有两个\"订阅级\"动作必须完成，否则 CI 部署必然失败。"))

BODY.append(heading("3.2 动作一：注册资源提供程序（Resource Provider）", 2))
BODY.append(para(run("是什么：", bold=True) + "资源提供程序是 Azure 里每种资源类型的\"服务插件\"。新订阅默认只注册了常用服务，第一次使用某类新服务时必须先注册，表示\"同意该服务在此订阅运行\"。注册是一次性的、免费的，注册不等于创建资源，也不产生费用。"))
BODY.append(para(run("为什么配：", bold=True) + "不注册 Microsoft.App 时，部署容器应用会直接报错：'Subscription ... is not registered for the Microsoft.App resource provider'。"))
BODY.append(table(
    ["提供程序", "管什么", "本项目是否需要"],
    [
        ["Microsoft.App", "Azure Container Apps 本体（容器应用、环境、副本、扩缩容）", "必须注册（CI 报错就是它）"],
        ["Microsoft.OperationalInsights", "Log Analytics 工作区（应用日志收集）", "建议注册，创建环境时可能自动关联日志工作区"],
        ["Microsoft.Insights", "监控指标与告警（Metrics / Alerts）", "建议注册，看 CPU/内存/请求数指标需要"],
    ],
    col_widths=[2800, 4200, 3000],
))
BODY.append(para(run("怎么配（CLI，Cloud Shell）：", bold=True), spacing_after=60))
BODY.append(code_block([
    'az provider register --namespace Microsoft.App --wait',
    'az provider show --namespace Microsoft.App --query registrationState -o tsv   # 输出 Registered 才继续',
]))
BODY.append(para(run("怎么配（门户）：", bold=True) + "订阅 →（点击订阅名进入详情）→ 资源提供程序 → 搜索 Microsoft.App → 选中 → 顶部\"注册\"；状态从 未注册 → 注册中 → 已注册（1-3 分钟，多点刷新）。顺手注册 Microsoft.OperationalInsights 和 Microsoft.Insights。", spacing_after=200))

BODY.append(heading("3.3 动作二：给服务主体分配角色（权限）", 2))
BODY.append(para(run("为什么配：", bold=True) + "OIDC 登录成功只代表\"身份验证通过\"，不代表\"有权限操作\"。服务主体默认看不到任何订阅/资源，必须显式授权，否则 deploy 阶段报 AuthorizationFailed / 没有权限查看订阅。"))
BODY.append(para(run("授权范围选择：", bold=True)))
BODY.append(bullet(run("订阅级别（/subscriptions/<id>）：最简单，任何资源都能操作，权限偏大。", bold=True)))
BODY.append(bullet(run("资源组级别（/subscriptions/<id>/resourceGroups/rg-travelmap）：最小权限，推荐，但前提是 rg-travelmap 已创建。", bold=True)))
BODY.append(para(run("怎么配（CLI，Cloud Shell，PowerShell 语法）：", bold=True), spacing_after=60))
BODY.append(code_block([
    '# 订阅级（最稳）',
    '$CLIENT_ID = "<AZUREAPPSERVICE_CLIENTID_... 的值>"',
    '$SUBSCRIPTION_ID = "<AZUREAPPSERVICE_SUBSCRIPTIONID_... 的值>"',
    'az role assignment create --assignee $CLIENT_ID --role Contributor --scope "/subscriptions/$SUBSCRIPTION_ID"',
    '',
    '# 资源组级（最小权限，更安全）',
    'az role assignment create --assignee $CLIENT_ID --role Contributor `',
    '  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-travelmap"',
]))
BODY.append(para(run("怎么配（门户）：", bold=True) + "订阅 → 访问控制 (IAM) → + 添加 → 添加角色分配 → 角色页签选 Contributor（参与者）→ 成员页签搜索应用注册名称或 Client ID → 查看 + 分配。", spacing_after=100))
BODY.append(para(run("验证：", bold=True) + "az role assignment list --assignee \"$CLIENT_ID\" --all -o table，看到一行 Contributor 即成功。", spacing_after=200))

BODY.append(heading("3.4 本项目踩过的坑（必读）", 2))
BODY.append(table(
    ["现象", "根因", "解决办法"],
    [
        ["Login to Azure 通过，但报没有订阅/权限不足", "服务主体只有 App Service 资源级权限，没有订阅/资源组 Contributor", "按 3.3 给服务主体分配 Contributor"],
        ["门户\"添加角色分配\"搜索\"参与者\"结果为空/全是细分角色", "门户角色向导的搜索+类别过滤组合有 bug", "清空过滤、搜英文 Contributor；或直接用 CLI 一条命令解决"],
        ["角色浏览页明明能看到\"参与者（常规）\"", "门户向导过滤异常（非角色不存在）", "Contributor 是 Azure 四大基础内置角色之一，不可能被移除，改走 CLI"],
    ],
    col_widths=[3400, 3300, 3300],
))

# ---------------- 第四章
BODY.append(heading("第四章 资源组（Resource Group）", 1))
BODY.append(heading("4.1 资源组是什么", 2))
BODY.append(para("资源组是资源的逻辑容器，用于统一管理生命周期、权限与成本归属。本项目所有 Azure 资源统一放在 rg-travelmap 下，便于一键排查和按组授权（最小权限授权通常就落在资源组上）。"))

BODY.append(heading("4.2 配置什么、为什么、怎么配", 2))
BODY.append(table(
    ["项", "说明"],
    [
        ["名称", "rg-travelmap"],
        ["区域", "eastasia（东亚），必须与 ci.yml 中 CAE_REGION=eastasia 一致"],
        ["为什么", "deploy action 的 scope、脚本的资源定位都基于它；区域与镜像/环境所在区域保持一致可避免跨区域访问"],
        ["怎么配", 'az group create --name rg-travelmap --location eastasia'],
    ],
    col_widths=[1600, 8400],
))
BODY.append(para(run("坑：", bold=True) + "如果选择资源组级授权（3.3 最小权限方案），必须先创建资源组再授权，否则 scope 不存在会报错。", spacing_after=200))

# ---------------- 第五章
BODY.append(heading("第五章 资源（Resources）", 1))
BODY.append(heading("5.1 资源清单总览", 2))
BODY.append(table(
    ["资源", "名称", "类型/规格", "入口类型"],
    [
        ["容器应用环境", "cae-travelmap", "Azure Container Apps 环境（区域 eastasia）", "—"],
        ["容器应用·后端", "user-service", "ghcr.io/walt-xie-hub/travelmap-user-service:latest，:8080", "internal"],
        ["容器应用·前端", "client", "ghcr.io/walt-xie-hub/travelmap-client:latest，:80", "internal"],
        ["容器应用·网关", "gateway", "ghcr.io/walt-xie-hub/travelmap-gateway:latest，:80", "external（唯一公网入口）"],
        ["数据库", "pg-travelmap", "PostgreSQL Flexible Server，Standard_B1ms（免费层）", "public access Enabled"],
    ],
    col_widths=[1800, 2300, 3600, 2300],
))

BODY.append(heading("5.2 容器应用环境 cae-travelmap", 2))
BODY.append(para(run("是什么：", bold=True) + "Container Apps 环境是容器应用共享的运行时与网络边界（类似 Kubernetes 集群）。环境名必须与 ci.yml 的 CAE_NAME=cae-travelmap 一致。"))
BODY.append(para(run("为什么配：", bold=True) + "所有容器应用都要在同一个环境内才能用内部域名互相访问；环境也是 min-replicas 0 缩容策略与免费额度的归属单位。", spacing_after=100))
BODY.append(code_block([
    'az containerapp env create --name cae-travelmap -g rg-travelmap --location eastasia',
]))

BODY.append(heading("5.3 三个容器应用详解", 2))
BODY.append(para(run("设计原则：只有 gateway 对外（external），user-service 与 client 对内（internal）。", bold=True, color="C00000")))
BODY.append(bullet(run("为什么：", bold=True) + "用户只应通过网关访问；后端和前端不直接暴露公网，减少攻击面、省公网 IP。"))
BODY.append(bullet(run("内部域名格式：", bold=True) + "<应用名>.internal.<环境名>.<区域>.azurecontainerapps.io，例如 user-service.internal.cae-travelmap.eastasia.azurecontainerapps.io"))

BODY.append(para(run("① user-service（后端，internal，:8080）", bold=True), spacing_after=60))
BODY.append(code_block([
    'az containerapp create \\',
    '  --name user-service -g rg-travelmap --environment cae-travelmap \\',
    '  --image ghcr.io/walt-xie-hub/travelmap-user-service:latest \\',
    '  --target-port 8080 --ingress internal --min-replicas 0 --max-replicas 3 \\',
    '  --secrets db-password="<你的PG密码>" \\',
    '  --env-vars \\',
    '    "ConnectionStrings__DefaultConnection=Host=pg-travelmap.postgres.database.azure.com;Port=5432;Database=appdb;Username=appuser;Password=secretref:db-password;Pooling=true" \\',
    '    "Db__Password=secretref:db-password"',
]))
BODY.append(bullet(run("secrets：", bold=True) + "db-password 以 secret 方式存储，环境变量通过 secretref:db-password 引用，避免明文密码出现在容器配置里。"))
BODY.append(bullet(run("min-replicas 0：", bold=True) + "空闲缩容到零，落在免费额度内；有请求时自动扩容到 max 3。"))

BODY.append(para(run("② client（前端，internal，:80）", bold=True), spacing_after=60))
BODY.append(code_block([
    'az containerapp create \\',
    '  --name client -g rg-travelmap --environment cae-travelmap \\',
    '  --image ghcr.io/walt-xie-hub/travelmap-client:latest \\',
    '  --target-port 80 --ingress internal --min-replicas 0 --max-replicas 3',
]))

BODY.append(para(run("③ gateway（网关，external，:80，唯一入口）", bold=True), spacing_after=60))
BODY.append(code_block([
    'az containerapp create \\',
    '  --name gateway -g rg-travelmap --environment cae-travelmap \\',
    '  --image ghcr.io/walt-xie-hub/travelmap-gateway:latest \\',
    '  --target-port 80 --ingress external --min-replicas 0 --max-replicas 3 \\',
    '  --env-vars \\',
    '    "USER_SERVICE_URL=http://user-service.internal.cae-travelmap.eastasia.azurecontainerapps.io" \\',
    '    "CLIENT_URL=http://client.internal.cae-travelmap.eastasia.azurecontainerapps.io"',
]))
BODY.append(bullet(run("环境变量的作用：", bold=True) + "gateway 的 nginx 模板用它们生成 /api/* 到后端、/* 到前端的反向代理地址。地址写错会导致页面 502。"))

BODY.append(para(run("获取公网地址：", bold=True) + "az containerapp show -n gateway -g rg-travelmap --query properties.configuration.ingress.fqdn -o tsv，输出 https://gateway.xxx.eastasia.azurecontainerapps.io 即网站入口。", spacing_after=200))

BODY.append(heading("5.4 数据库 PostgreSQL（pg-travelmap）", 2))
BODY.append(code_block([
    'az postgres flexible-server create -g rg-travelmap \\',
    '  --name pg-travelmap --sku-name Standard_B1ms --tier Burstable \\',
    '  --storage-size 32 --public-access Enabled \\',
    '  --admin-user appuser --admin-password "<你的强密码>" --yes',
]))
BODY.append(bullet(run("为什么 B1ms：", bold=True) + "Burstable 突发性能机型，12 个月内免费；到期转付费，需设提醒。"))
BODY.append(bullet(run("连接串：", bold=True) + "Host=pg-travelmap.postgres.database.azure.com;Port=5432;Database=appdb;Username=appuser;Password=...(通过 secret 注入)。"))

# ---------------- 第六章
BODY.append(heading("第六章 GitHub 侧配套配置", 1))
BODY.append(heading("6.1 仓库 Secrets（OIDC 三件套）", 2))
BODY.append(para("这三个 secret 由 Azure 部署向导自动生成，存放在仓库 Settings → Secrets and variables → Actions，供 ci.yml 的 azure/login 使用："))
BODY.append(code_block([
    'AZUREAPPSERVICE_CLIENTID_3E4B653535F64E518FC068312A4ACF78      # Application (client) ID',
    'AZUREAPPSERVICE_TENANTID_A2B2CD6D4C5C4A77B27AE5DA70E292AE      # Directory (tenant) ID',
    'AZUREAPPSERVICE_SUBSCRIPTIONID_12264A5C94164B61945E7CA2E9EEE253  # Subscription ID',
]))
BODY.append(bullet(run("Client ID：", bold=True) + "对应第二章 App Registration，是给服务主体授权（3.3）时的 assignee。"))
BODY.append(bullet(run("Tenant ID：", bold=True) + "对应第二章租户。"))
BODY.append(bullet(run("Subscription ID：", bold=True) + "对应第三章订阅，是授权 scope 与角色分配里的订阅 ID。"))

BODY.append(heading("6.2 ci.yml 关键设计点", 2))
BODY.append(table(
    ["配置项", "作用"],
    [
        ["镜像命名空间：ghcr.io/${{ github.repository_owner }}/travelmap-*", "镜像跟随仓库 owner。仓库在组织 walt-xie-hub 下时自动推送到 ghcr.io/walt-xie-hub/*，迁移仓库后不用改代码"],
        ["包可见性接口：orgs/<owner>/packages/container/<img>", "组织仓库的 GHCR 包属于 org 命名空间，必须调用 orgs 接口（user/packages 只能改个人包）；并显式传 GH_TOKEN"],
        ["deploy job 条件：github.event_name != 'pull_request'", "PR 只构建测试不部署"],
        ["permissions: id-token: write", "OIDC 登录 Azure 需要请求短期 JWT"],
        ["container-apps-deploy-action × 3", "只更新已存在应用的镜像，不负责创建（资源必须预先建好，见第五章）"],
    ],
    col_widths=[5000, 5000],
))

BODY.append(heading("6.3 常见 GitHub 侧问题", 2))
BODY.append(table(
    ["现象", "根因", "解决办法"],
    [
        ["buildx 报 502 Bad Gateway", "GitHub Actions 基础设施偶发网络抖动", "点 Re-run failed jobs 重跑即可，通常一次就过"],
        ["推镜像报权限错误（无法 push 到 ghcr.io/waltxie1986/...）", "仓库迁到组织后镜像命名空间还是个人", "ci.yml 改用 ${{ github.repository_owner }}"],
        ["设置包可见性失败", "调用了 user/packages 接口，组织包要用 orgs 接口", "改用 orgs/<owner>/packages/container/<img> 并传 GH_TOKEN"],
        ["本地 git push 提示仓库迁移", "git remote 还指向旧地址", 'git remote set-url origin https://github.com/walt-xie-hub/TravelHistoryMap.git'],
    ],
    col_widths=[3200, 3300, 3500],
))

# ---------------- 第七章
BODY.append(heading("第七章 全量问题排查速查表", 1))
BODY.append(table(
    ["序号", "报错/现象", "定位层级", "根因", "解决动作"],
    [
        ["1", "Login to Azure 失败：token 里仓库路径是组织，Azure 只信任旧仓库", "租户", "Federated Credential subject 还是旧仓库", "更新 subject 为 repo:walt-xie-hub@317605909/TravelHistoryMap:ref:refs/heads/main"],
        ["2", "OIDC subject 不匹配（master vs main）", "租户", "Azure 配的分支是 master", "Branch/主题标识符改为 main"],
        ["3", "Login 通过但报无订阅/权限不足", "订阅", "服务主体没有 Contributor", "az role assignment create --assignee <CLIENT_ID> --role Contributor --scope ..."],
        ["4", "门户搜不到\"参与者\"", "订阅（门户 UI）", "门户角色向导过滤 bug", "清过滤搜 Contributor 或直接用 CLI"],
        ["5", "deploy 报 not registered for Microsoft.App", "订阅", "资源提供程序未注册", "az provider register -n Microsoft.App --wait（顺手注册 OperationalInsights / Insights）"],
        ["6", "deploy 报 Container App not found", "资源", "Azure 上还没建 3 个容器应用", "运行仓库根目录 deploy-azure.sh 初始化"],
        ["7", "推镜像权限错误 / 包可见性失败", "GitHub", "镜像命名空间与包接口未跟随组织", "ci.yml 用 ${{ github.repository_owner }} + orgs/.../packages"],
        ["8", "buildx 502", "GitHub", "基础设施偶发网络抖动", "Re-run failed jobs"],
        ["9", "访问网关 502 / 页面白屏", "资源", "gateway 的 USER_SERVICE_URL / CLIENT_URL 内部域名或环境名/区域不对", "核对 cae-travelmap / eastasia 与内部域名格式"],
    ],
    col_widths=[500, 2900, 1000, 2800, 2800],
))

# ---------------- 附录
BODY.append(heading("附录 A 一次性初始化脚本（deploy-azure.sh 等价命令）", 1))
BODY.append(code_block([
    '# 0) 注册资源提供程序（一次）',
    'az provider register --namespace Microsoft.App --wait',
    'az provider register --namespace Microsoft.OperationalInsights --wait',
    'az provider register --namespace Microsoft.Insights --wait',
    '',
    '# 1) 资源组 + 环境',
    'az group create --name rg-travelmap --location eastasia',
    'az containerapp env create --name cae-travelmap -g rg-travelmap --location eastasia',
    '',
    '# 2) PostgreSQL 免费层',
    'az postgres flexible-server create -g rg-travelmap --name pg-travelmap \\',
    '  --sku-name Standard_B1ms --tier Burstable --storage-size 32 \\',
    '  --public-access Enabled --admin-user appuser --admin-password "<密码>" --yes',
    '',
    '# 3) 服务主体授权（Cloud Shell PowerShell 语法）',
    '$CLIENT_ID="<CLIENT_ID>"; $SUBSCRIPTION_ID="<SUBSCRIPTION_ID>"',
    'az role assignment create --assignee $CLIENT_ID --role Contributor --scope "/subscriptions/$SUBSCRIPTION_ID"',
    '',
    '# 4) 三个容器应用（镜像路径：ghcr.io/walt-xie-hub/*）——见 5.3',
    '# 5) 触发 CI：push main 或手动 Run workflow',
]))

BODY.append(heading("附录 B 常用验证命令", 1))
BODY.append(code_block([
    'az provider show --namespace Microsoft.App --query registrationState -o tsv   # 应为 Registered',
    'az role assignment list --assignee "$CLIENT_ID" --all -o table                # 应有 Contributor 一行',
    'az containerapp show -n gateway -g rg-travelmap --query properties.configuration.ingress.fqdn -o tsv  # 公网入口',
    'az containerapp list -g rg-travelmap -o table                                 # 3 个应用是否就绪',
]))

BODY.append(heading("附录 C 关键命名对照表", 1))
BODY.append(table(
    ["用途", "命名", "对应位置"],
    [
        ["资源组", "rg-travelmap", "Azure / ci.yml / deploy-azure.sh"],
        ["容器应用环境", "cae-travelmap", "Azure / ci.yml CAE_NAME"],
        ["区域", "eastasia", "ci.yml CAE_REGION / 资源创建命令"],
        ["镜像前缀", "ghcr.io/walt-xie-hub/", "ci.yml / deploy-azure.sh"],
        ["数据库", "pg-travelmap", "连接串 Host"],
        ["OIDC 三个 secret", "AZUREAPPSERVICE_*", "GitHub 仓库 Secrets"],
        ["Federated subject", "repo:walt-xie-hub@317605909/TravelHistoryMap:ref:refs/heads/main", "Microsoft Entra ID"],
    ],
    col_widths=[2100, 3600, 4300],
))

BODY.append(spacer())
BODY.append(para(run("—— 文档结束 ——", italic=True, color="808080"), spacing_after=0))

# ---------------------------------------------------------------- docx 打包

CONTENT_TYPES = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
</Types>'''

RELS = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="word/styles.xml"/>
</Relationships>'''

DOC_RELS = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>'''

STYLES = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:docDefaults><w:rPrDefault><w:rPr>
<w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:eastAsia="微软雅黑"/>
<w:sz w:val="21"/><w:szCs w:val="21"/>
</w:rPr></w:rPrDefault></w:docDefaults>
<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/>
<w:pPr><w:keepNext/><w:spacing w:before="360" w:after="200"/><w:outlineLvl w:val="0"/></w:pPr>
<w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:eastAsia="微软雅黑"/><w:b/><w:color w:val="1F4E79"/><w:sz w:val="32"/><w:szCs w:val="32"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/>
<w:pPr><w:keepNext/><w:spacing w:before="240" w:after="160"/><w:outlineLvl w:val="1"/></w:pPr>
<w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:eastAsia="微软雅黑"/><w:b/><w:color w:val="2E74B5"/><w:sz w:val="26"/><w:szCs w:val="26"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading3"><w:name w:val="heading 3"/>
<w:pPr><w:keepNext/><w:spacing w:before="200" w:after="120"/><w:outlineLvl w:val="2"/></w:pPr>
<w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:eastAsia="微软雅黑"/><w:b/><w:color w:val="2E74B5"/><w:sz w:val="24"/><w:szCs w:val="24"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="CodeBlock"><w:name w:val="CodeBlock"/>
<w:pPr><w:spacing w:after="0"/><w:ind w:left="200"/><w:shd w:val="clear" w:color="auto" w:fill="F2F2F2"/></w:pPr>
<w:rPr><w:rFonts w:ascii="Consolas" w:hAnsi="Consolas" w:eastAsia="微软雅黑"/><w:sz w:val="19"/><w:szCs w:val="19"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="ListBullet"><w:name w:val="ListBullet"/>
<w:pPr><w:spacing w:after="60"/><w:ind w:left="480" w:hanging="240"/></w:pPr>
<w:rPr><w:rFonts w:eastAsia="微软雅黑"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="ListBullet2"><w:name w:val="ListBullet2"/>
<w:pPr><w:spacing w:after="60"/><w:ind w:left="900" w:hanging="240"/></w:pPr>
<w:rPr><w:rFonts w:eastAsia="微软雅黑"/></w:rPr></w:style>
<w:style w:type="table" w:styleId="TableGrid"><w:name w:val="TableGrid"/>
<w:tblPr><w:tblBorders>
<w:top w:val="single" w:sz="4" w:color="BFBFBF"/><w:left w:val="single" w:sz="4" w:color="BFBFBF"/>
<w:bottom w:val="single" w:sz="4" w:color="BFBFBF"/><w:right w:val="single" w:sz="4" w:color="BFBFBF"/>
<w:insideH w:val="single" w:sz="4" w:color="BFBFBF"/><w:insideV w:val="single" w:sz="4" w:color="BFBFBF"/>
</w:tblBorders></w:tblPr>
<w:tblCellMar><w:top w:w="40" w:type="dxa"/><w:left w:w="80" w:type="dxa"/>
<w:bottom w:w="40" w:type="dxa"/><w:right w:w="80" w:type="dxa"/></w:tblCellMar></w:style>
</w:styles>'''

DOCUMENT = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
<w:body>%s
<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="708" w:footer="708" w:gutter="0"/></w:sectPr>
</w:body></w:document>''' % "".join(BODY)


def main():
    out_dir = os.path.dirname(os.path.abspath(__file__))
    out_path = os.path.join(out_dir, "TravelMap-Azure部署配置指南.docx")
    with zipfile.ZipFile(out_path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", CONTENT_TYPES)
        z.writestr("_rels/.rels", RELS)
        z.writestr("word/document.xml", DOCUMENT)
        z.writestr("word/styles.xml", STYLES)
        z.writestr("word/_rels/document.xml.rels", DOC_RELS)
    print("OK -> %s" % out_path)


if __name__ == "__main__":
    main()
