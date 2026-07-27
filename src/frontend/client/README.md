# Travel Map · Client

Angular 22（standalone + zoneless）前端工程，采用**特性化多模块（feature-based）**架构。

## 目录结构

```
src/
├── environments/            # 环境配置（dev / prod，含 apiBaseUrl）
└── app/
    ├── core/                # 全局单例：拦截器、全局服务、通用模型
    │   ├── interceptors/    #   api-base-url（基础URL拼接）、error（统一错误处理）
    │   ├── services/        #   config（环境读取）、notification（全局通知）
    │   └── models/          #   跨模块通用模型（如分页）
    ├── shared/              # 可复用 UI：page-header / loading-spinner / empty-state
    ├── layout/              # 布局外壳：main-layout（顶部导航 + 内容区 + 通知）
    ├── features/            # 业务特性模块（均为懒加载）
    │   ├── home/            #   首页
    │   ├── map/             #   地图（占位，待接入 Leaflet/Mapbox）
    │   ├── users/           #   用户管理，对接后端 /api/users
    │   └── not-found/       #   404
    ├── app.config.ts        # 应用级 Provider（Router / HttpClient + 拦截器）
    └── app.routes.ts        # 顶层路由（布局外壳 + 懒加载子模块）
```

路径别名（见 `tsconfig.json`）：`@core/*`、`@shared/*`、`@layout/*`、`@features/*`、`@env/*`。

新增一个业务模块的步骤：在 `features/<name>/` 下创建 `pages/` 组件与 `<name>.routes.ts`，再在 `app.routes.ts` 中以 `loadChildren` 懒加载挂载即可。

---

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 22.0.3.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Vitest](https://vitest.dev/) test runner, use the following command:

```bash
ng test
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
