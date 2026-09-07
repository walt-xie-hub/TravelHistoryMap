# Logs

项目日志按生成来源分类：

- `docker-build/`: Docker 镜像构建输出和错误日志。
- `verification/`: 本地服务、健康检查和功能验证日志。
- `git/`: Git 远程、仓库连接和版本诊断日志。

应用运行日志默认由 Docker 输出到容器标准输出，并由 OpenTelemetry 转发到 Collector、Loki 和 Jaeger；查看方式见项目根目录 `README.md`。需要保存运行日志文件时，请放入 `log/runtime/`，不要放在仓库根目录。
