# Map page presentation: merged same-spot markers, arrival-window filter chips, auto-fit viewport

`/map` 展示层收敛规则（Angular 22 zoneless + signals，只读，无录入）：

- **同一地点多次停留 → 同坐标合并标注**：单用户记录量通常 <100，不做聚合插件；把坐标近似相等（容差内，按 4 位小数分组）的多条记录折叠为一枚标注，点击弹出该点全部停留明细；列表/气泡行可高亮联动。单条记录仍对应地图一个点（与 ADR-0001 一致），合并仅是视觉折叠。
- **侧边时间线列表**：按 `arrivedAt` 倒序；提供筛选 chips（全部 / 近30天 / 今年 / 自定义起止）。自定义走原生日期输入。筛选一律映射为后端 `from/to`（到达时间窗，UTC 时刻）**服务端过滤**后重新拉取，不把"已过滤列表"留在客户端拼假。
- **默认视野**：进入页面、切换用户、筛选变化后 `setFitView` 自动适配当前标注集合（带 padding）；无标注时显示空态 + 全国默认视野；提供手动"回到全览"。不做视野记忆（localStorage 只存上次选中的 userId）。
- **进行中停留**（`departedAt == null`）在标注与列表中视觉区分；其余展示约定（WGS-84 存储、GCJ-02 转换、AMap JSAPI 2.0 动态加载、密钥占位）见 ADR-0003。
