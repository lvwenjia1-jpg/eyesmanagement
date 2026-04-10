# 订单文本训练工具

## 组成
- `OrderTextTrainer.Core`：可复用解析库，生成 `OrderTextTrainer.Core.dll`
- `WpfApp11`：训练/调试界面，可粘贴文本、查看解析 JSON、追加规则、保存样本

## 规则与样本
程序首次启动会在运行目录生成：
- `parser-rules.json`
- `training-samples.jsonl`
- `product-catalog.json`
- `product-matches.json`

你可以持续把新文本保存为样本，再把未识别商品补成标准别名规则。
也可以通过“导入商品表”把 `xlsx` 商品主数据导入进来，按商品型号和度数做精确匹配；未命中的项会在右下角列表里用下拉框手工选择。

## 其他项目接入
```csharp
using OrderTextTrainer.Core.Services;

var repository = new RuleRepository();
var rules = repository.LoadOrCreate(@"parser-rules.json");
var parser = new OrderTextParser();
var result = parser.Parse(rawText, rules);
```

## 当前能力
- 识别姓名、电话、地址、品牌、抛型
- 识别商品名、度数、数量、缺货、赠品
- 支持多订单文本按块拆分
- 支持别名规则持续追加
- 支持导入商品表并做商品编码精确匹配
- 支持对未精确匹配商品进行人工下拉确认并保存覆盖

## 本地联动部署
- 启动 API：`dotnet run --project MainApi`
- 默认地址：`http://127.0.0.1:5249`，根路径会直接提供仪表盘静态页
- 登录页：`http://127.0.0.1:5249/login.html`
- 默认管理员：账号 `admin`，密码 `123456`，机器码 `DEMO-PC-001`
- 仪表盘已联动这些接口：`/api/auth/login`、`/api/users`、`/api/machines`、`/api/uploads`、`/api/product-catalog`
- WPF 主程序在“接口配置”里填 `MainApi` 地址、账号、密码、机器码后，可同步商品编码表和上传记录

## Ubuntu 部署
- 方式一：直接发布
- 发布命令：`dotnet publish MainApi/MainApi.csproj -c Release -o publish`
- 运行命令：`ASPNETCORE_URLS=http://0.0.0.0:8080 dotnet MainApi.dll`
- 建议使用 `nginx` 反向代理到 `8080`，并把 HTTPS 终止放在 `nginx`
- 程序已支持转发头，并在发布时自动携带 `Dasbord` 静态页面

## Docker 部署
- 构建镜像：`docker build -f MainApi/Dockerfile -t eyesmanagement-mainapi .`
- 启动容器：`docker run -d --name eyesmanagement -p 8080:8080 eyesmanagement-mainapi`
- 首次运行会在容器内生成 SQLite 数据库，可再挂载 `/app/App_Data`

## 后续可扩展
- 增加更强的多订单切分器
- 引入评分模型或 OCR 校正
- 增加“人工确认后反向训练”界面
- 输出为数据库规则、JSON 包或独立 API

## Ubuntu 模板
- deploy/ubuntu/nginx-mainapi.conf：nginx 反向代理模板
- deploy/ubuntu/nginx-upgrade-map.conf：WebSocket/Upgrade 映射
- deploy/ubuntu/mainapi.service：systemd 服务模板
- deploy/ubuntu/mainapi.env.example：环境变量模板
- deploy/ubuntu/README.md：Ubuntu 部署步骤

- deploy/ubuntu/nginx-mainapi-ssl.conf.example：HTTPS 版 nginx 模板
- deploy/ubuntu/install.sh：Ubuntu 一键安装脚本
