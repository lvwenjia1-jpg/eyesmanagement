# Ubuntu 部署说明

## Docker 部署入口
如果你使用 Docker 部署（而不是 systemd 方式），请直接看：
- `deploy/ubuntu/docker/README.md`
- `deploy/ubuntu/docker/docker-compose.yml`

## 目录约定
- 发布目录：`/opt/eyesmanagement/mainapi`
- 环境变量：`/etc/eyesmanagement/mainapi.env`
- `systemd` 服务：`/etc/systemd/system/mainapi.service`
- `nginx` 站点：`/etc/nginx/sites-available/eyesmanagement.conf`

## 1. 安装运行环境
```bash
sudo apt-get update
sudo apt-get install -y nginx rsync
```

安装 `.NET 8 ASP.NET Core Runtime` 后再继续。

## 2. 发布程序
在开发机执行：
```bash
dotnet publish MainApi/MainApi.csproj -c Release -o publish/mainapi
```

上传：
- `publish/mainapi/`
- `deploy/ubuntu/`

## 3. 一键安装
```bash
cd deploy/ubuntu
sudo bash install.sh /path/to/publish/mainapi your-domain.com
```

脚本会自动完成：
- 同步发布文件到 `/opt/eyesmanagement/mainapi`
- 初始化 `/etc/eyesmanagement/mainapi.env`
- 安装 `systemd` 服务
- 安装 `nginx` 反向代理配置
- 重启 `mainapi` 并 reload `nginx`

## 4. 手工修改环境变量
```bash
sudo nano /etc/eyesmanagement/mainapi.env
sudo systemctl restart mainapi
```

至少修改：
- `Jwt__SigningKey`
- `BootstrapAdmin__Password`

可选初始化数据：
- `DashboardSeed__Enabled=true`
- `DashboardSeed__ResetExistingData=false`
- `DashboardSeed__BusinessGroupCount=6`
- `DashboardSeed__OrdersPerGroup=12`

## 5. 查看运行状态
```bash
sudo systemctl status mainapi
sudo journalctl -u mainapi -f
curl http://127.0.0.1:8080/api/system/status
```

## 6. 访问验证
- Swagger UI：`http://<你的域名或IP>/swagger`
- 健康检查：`http://<你的域名或IP>/api/system/status`

首次启动后，如果数据库为空，系统会自动初始化：
- 管理员账号
- 用户模拟数据
- 机器码模拟数据
- 业务群模拟数据
- 订单及订单商品模拟数据

## 7. HTTPS（Certbot）
安装：
```bash
sudo apt-get update
sudo apt-get install -y certbot python3-certbot-nginx
```

申请证书：
```bash
sudo certbot --nginx -d your-domain.com -d www.your-domain.com
```

续期测试：
```bash
sudo certbot renew --dry-run
```

## 8. 更新发布
```bash
sudo bash deploy/ubuntu/install.sh /path/to/new/publish/mainapi your-domain.com
```

## 9. 相关模板
- `deploy/ubuntu/install.sh`
- `deploy/ubuntu/mainapi.service`
- `deploy/ubuntu/mainapi.env.example`
- `deploy/ubuntu/nginx-mainapi.conf`
- `deploy/ubuntu/nginx-mainapi-ssl.conf.example`
- `deploy/ubuntu/nginx-upgrade-map.conf`
