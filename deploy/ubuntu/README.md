# Ubuntu 部署说明

## 目录约定
- 发布目录：`/opt/eyesmanagement/mainapi`
- 环境变量：`/etc/eyesmanagement/mainapi.env`
- systemd 服务：`/etc/systemd/system/mainapi.service`
- nginx 站点：`/etc/nginx/sites-available/eyesmanagement.conf`

## 1. 安装运行环境
```bash
sudo apt-get update
sudo apt-get install -y nginx rsync
```

安装 .NET 6 Runtime / ASP.NET Core Runtime 后再继续。

## 2. 发布程序
在开发机执行：
```bash
dotnet publish MainApi/MainApi.csproj -c Release -o publish/mainapi
```

把仓库里的这些文件上传到服务器：
- `publish/mainapi/`
- `deploy/ubuntu/`

## 3. 一键安装
```bash
cd deploy/ubuntu
sudo bash install.sh /path/to/publish/mainapi your-domain.com
```

说明：
- 第一个参数是发布目录
- 第二个参数是 `nginx server_name`，没有域名时可省略

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
- `BootstrapAdmin__MachineCode`

## 5. 查看运行状态
```bash
sudo systemctl status mainapi
sudo journalctl -u mainapi -f
curl http://127.0.0.1:8080/api/system/status
```

## 6. HTTP 访问验证
- 登录页：`http://<你的域名或IP>/login.html`
- Swagger：`http://<你的域名或IP>/swagger`
- 健康检查：`http://<你的域名或IP>/api/system/status`

## 7. HTTPS（Certbot）
### 方式一：推荐，直接让 Certbot 改 nginx
先确认：
- 域名已解析到服务器公网 IP
- 80 端口可访问

安装证书工具：
```bash
sudo apt-get update
sudo apt-get install -y certbot python3-certbot-nginx
```

申请并自动改 nginx：
```bash
sudo certbot --nginx -d your-domain.com -d www.your-domain.com
```

自动续期测试：
```bash
sudo certbot renew --dry-run
```

### 方式二：手工 SSL 模板
参考文件：`deploy/ubuntu/nginx-mainapi-ssl.conf.example`

把里面这些值替换成你的真实配置：
- 域名
- `fullchain.pem`
- `privkey.pem`

然后覆盖 nginx 站点配置并 reload：
```bash
sudo cp deploy/ubuntu/nginx-mainapi-ssl.conf.example /etc/nginx/sites-available/eyesmanagement.conf
sudo nginx -t
sudo systemctl reload nginx
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
