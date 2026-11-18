# BÀI 3 – PHÁT TRIỂN ỨNG DỤNG PHÂN TÁN: SIGNALR

Bộ project gồm hai solution theo tài liệu bài giảng:

1. `PingSignalR`: một máy chủ SignalR và máy khách Console, minh họa Echo, Broadcast, Group, vòng đời kết nối và reconnect.
2. `RealtimeSubmissionSystem`: REST + SignalR cho EduSubmit; máy chủ mô phỏng chấm nền và đẩy `processing`, `graded` tới nhóm `submission:{id}`.

> Yêu cầu môi trường: .NET SDK 10.0. Gói SignalR Client theo slide: `10.0.15`.
> Hai project dùng cổng cố định: PingSignalR `5200`; EduSubmit `5000`.

## 1. Cấu trúc thư mục

```text
Bài 3/
├── README.md
├── .gitignore
├── Images/                         # sơ đồ minh họa luồng xử lý
├── MinhChung_ThucTe/               # nơi lưu ảnh chụp thật từ máy Windows
├── PingSignalR/
│   ├── PingSignalR.slnx
│   └── src/
│       ├── Ping.Api/
│       └── Ping.Client/
└── RealtimeSubmissionSystem/
    ├── RealtimeSubmissionSystem.slnx
    ├── requests.http                # 9 ca kiểm tra theo slide
    ├── nop-bai.json
    └── src/
        ├── Submission.RealtimeApi/
        └── Submission.RealtimeClient/
```

## 2. Yêu cầu cài đặt

- Windows 10/11.
- .NET SDK 10.0 (`dotnet --version`).
- Visual Studio Code hoặc Visual Studio.
- Internet trong lần `dotnet restore` đầu tiên để tải NuGet.

Kiểm tra SDK:

```powershell
dotnet --version
dotnet --list-sdks
```

Nếu máy trường chặn DLL bằng Application Control, không tự tắt chính sách bảo mật. Hãy nhờ quản trị viên kiểm tra chính sách cho phép chạy ứng dụng .NET.

## 3. Chạy PingSignalR

Mở PowerShell tại thư mục `Bài 3`.

### Terminal 1 – máy chủ

```powershell
cd .\PingSignalR
dotnet restore
dotnet build
dotnet run --project src/Ping.Api --urls http://localhost:5200
```

Giữ Terminal 1 mở. Kết quả cần có `Now listening on: http://localhost:5200`.

### Terminal 2 – máy khách An

```powershell
cd ".\Bài 3\PingSignalR"
dotnet run --project src/Ping.Client -- http://localhost:5200 An
```

### Terminal 3 – máy khách Bình

```powershell
cd ".\Bài 3\PingSignalR"
dotnet run --project src/Ping.Client -- http://localhost:5200 Binh
```

Nếu terminal đã mở tại `Bài 3`, chỉ cần `cd .\PingSignalR`. Nếu đang ở `PingSignalR`, không thêm `PingSignalR` lần nữa.

Kiểm tra REST:

```powershell
curl.exe http://localhost:5200/
```

Mong đợi: `{"service":"PingApi","status":"running"}`.

Kiểm tra negotiate:

```powershell
curl.exe -X POST "http://localhost:5200/hubs/ping/negotiate?negotiateVersion=1"
```

Mong đợi JSON có `connectionId` và `availableTransports`.

### Thử reconnect

Giữ hai máy khách chạy. Nhấn `Ctrl+C` ở Terminal 1 để dừng máy chủ, sau đó chạy lại lệnh `dotnet run` máy chủ. Máy khách sẽ hiển thị các dòng `[Lifecycle] Dang ket noi lai...` và `[Lifecycle] Da ket noi lai: ...`. `ConnectionId` mới khác ID cũ; code máy khách gọi `JoinGroup` lại sau reconnect.

## 4. Chạy EduSubmit Realtime

Mở Terminal 1 tại thư mục `Bài 3`:

```powershell
cd .\RealtimeSubmissionSystem
dotnet restore
dotnet build
dotnet run --project src/Submission.RealtimeApi --urls http://localhost:5000
```

Giữ máy chủ chạy. Mở Terminal 2:

```powershell
cd ".\Bài 3\RealtimeSubmissionSystem"
dotnet run --project src/Submission.RealtimeClient -- http://localhost:5000 SV001
```

Kết quả mong đợi của máy khách:

```text
Da ket noi SignalR.
Da tao luot nop: <guid>
Trang thai REST ban dau: received
Thong bao SignalR: <id> -> processing
Thong bao SignalR: <id> -> graded
Trang thai doc lai bang REST: graded
```

Thời gian mô phỏng: khoảng 1 giây tới `processing`, thêm khoảng 2 giây tới `graded`. POST trả `201 Created` ngay khi tạo lượt nộp; tác vụ nền tiếp tục sau đó.

## 5. Kiểm tra 9 request

Mở `RealtimeSubmissionSystem/requests.http` bằng VS Code và dùng REST Client để gửi từng request. Hoặc chạy tương đương bằng `curl.exe`.

| # | Ca kiểm tra | Kết quả mong đợi |
|---|---|---:|
| 1 | `GET /` | 200 |
| 2 | `GET /api/assignments` | 200 |
| 3 | POST hợp lệ `/api/assignments/1/submissions` | 201 + Location |
| 4 | GET lượt nộp vừa tạo | 200; sau vài giây là `graded` |
| 5 | POST thiếu `studentCode`, `repositoryUrl` | 400 |
| 6 | POST assignment `999` | 404 |
| 7 | GET GUID không tồn tại | 404 |
| 8 | POST `/hubs/submissions/negotiate` | 200 |
| 9 | POST sai `/hub/submissions/negotiate` | 404 |

Lưu ý: request số 4 dùng biến response của request số 3 trong VS Code REST Client. Gửi lại request số 4 sau khoảng 1 giây và sau khoảng 4–5 giây để quan sát `received → processing → graded`.

## 6. Giải thích ba câu hỏi đầu giờ

### 6.1 Máy chủ có thể chủ động báo cho máy khách không?

Có. Máy khách mở trước một kết nối lâu dài tới SignalR Hub. Khi trạng thái thay đổi, máy chủ gửi thông điệp qua `IHubContext` tới nhóm `submission:{id}` bằng `SendAsync`. Máy khách đã đăng ký `On<SubmissionStatusMessage>` sẽ nhận được thông báo mà không cần liên tục hỏi máy chủ.

### 6.2 Đã có REST rồi, vì sao còn cần SignalR? Hai thứ chia việc thế nào?

REST dùng để tạo và đọc tài nguyên với mã trạng thái rõ ràng: tạo lượt nộp (`201`), dữ liệu không hợp lệ (`400`), không tìm thấy (`404`), và đọc trạng thái hiện tại. SignalR đẩy các thay đổi phát sinh sau yêu cầu ban đầu tới máy khách theo thời gian thực. SignalR không thay thế REST: REST là nguồn sự thật; SignalR giúp biết thay đổi nhanh hơn.

### 6.3 Nếu đứt mạng đúng lúc máy chủ báo “đã chấm xong” thì sao?

Thông điệp SignalR có thể bị lỡ; SignalR không lưu tin để gửi bù. Khi kết nối lại, `ConnectionId` mới được cấp và máy khách cần gọi lại `WatchSubmission(id)`. Sau đó máy khách gọi `GET /api/submissions/{id}` để lấy trạng thái mới nhất từ REST. Quy tắc: **Push để nhanh, REST để chắc.**

## 7. Năm thí nghiệm phân tán

| # | Thí nghiệm | Dự đoán | Kết quả cần quan sát | Giải thích |
|---|---|---|---|---|
| 1 | Hai máy khách theo dõi hai lượt nộp khác nhau | Mỗi máy chỉ nhận trạng thái lượt mình theo dõi | Hai nhóm `submission:{id}` tách biệt | Không dùng `Clients.All`, tránh lộ trạng thái lượt nộp khác |
| 2 | Tắt máy chủ sau khi máy khách đã kết nối | Client thử reconnect | Có `Reconnecting`, sau khi bật server có `Reconnected` | Tự reconnect chỉ áp dụng cho kết nối đã mở; ID kết nối đổi |
| 3 | Sai đường dẫn `/hub/submissions` | Negotiate thất bại | HTTP 404 | Đường dẫn client phải khớp `MapHub("/hubs/submissions")` |
| 4 | Đổi tên sự kiện `SubmissionStatusChanged` ở client thành tên sai | Không nhận được thông báo, thường không có lỗi rõ ràng | Không thấy dòng trạng thái | Tên sự kiện hai phía phải khớp chính xác |
| 5 | Client vào nhóm muộn sau khi trạng thái đã `graded` | Không nhận lại thông báo cũ | SignalR không phát lại; GET REST vẫn trả `graded` | SignalR không lưu lịch sử; REST là nguồn trạng thái hiện tại |

Khi làm báo cáo, ghi thêm ba cột **Dự đoán – Kết quả thật – Giải thích** sau khi tự chạy từng thí nghiệm. Không ghi kết quả chưa quan sát như thể đã chạy.

## 8. Ảnh minh chứng

- `Images/` chứa sơ đồ minh họa kiến trúc và luồng thông điệp, không phải ảnh chụp màn hình thực thi.
- Chụp ảnh thật từ máy Windows và lưu vào `MinhChung_ThucTe/` theo danh sách trong `MinhChung_ThucTe/README.txt`.
- Nên có ảnh: build hai solution, Ping server, hai Ping clients, reconnect, EduSubmit server, EduSubmit client nhận `graded`, và 9 request HTTP.

## 9. Git và nộp bài

Không commit `bin/`, `obj/`. Theo yêu cầu slide, tạo tối thiểu 8 commit theo từng bước. Ví dụ:

```powershell
git add "Vàng Văn Duy - dtc245200361/Bài 3"
git commit -m "Bai3 B1: tao hai solution"
git commit -am "Bai3 B2: hoan thien PingSignalR"
git commit -am "Bai3 B3: them reconnect"
git commit -am "Bai3 B4: tao RealtimeSubmissionSystem"
git commit -am "Bai3 B5: them REST submission"
git commit -am "Bai3 B6: them SignalR hub va background grading"
git commit -am "Bai3 B7: them client va requests"
git commit -am "Bai3 B8: bo sung README va minh chung"
```

Các commit trên chỉ là ví dụ thứ tự; để có lịch sử commit đúng, hãy commit từng phần khi thực hiện, không tạo commit giả hồi tố. Mở Pull Request theo tên `Bai3 - MSSV - Họ tên`, chỉ sửa thư mục cá nhân theo hướng dẫn lớp.

## 10. Giới hạn xác minh

Hai solution đã được kiểm tra bằng .NET SDK 10.0; cả hai đều build thành công. Chỉ thêm ảnh chụp thực tế sau khi tự chạy ứng dụng trên máy Windows.
