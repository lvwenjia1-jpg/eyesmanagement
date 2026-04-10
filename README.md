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

## 后续可扩展
- 增加更强的多订单切分器
- 引入评分模型或 OCR 校正
- 增加“人工确认后反向训练”界面
- 输出为数据库规则、JSON 包或独立 API
