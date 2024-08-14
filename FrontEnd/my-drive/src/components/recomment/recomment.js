import React from 'react';
import './recomment.scss';

export default function Recomment() {
    return (
        <div>
            <h3>Chào mùng bạn đến với Drive</h3>
            <div>
                <span>Được đề xuất</span>
                <button>Tệp</button>
                <button>Thư mục</button>
                <button>list</button>
                <button>grid</button>
            </div>
            <div>
                <table>
                    <tr>
                        <th>Tên</th>
                        <th>Lý do tài liệu đề xuất</th>
                        <th>Chủ sở hữu</th>
                    </tr>
                    <tr>
                        <td>Thiết kế ứng dụng chứng khoán với ASP .net core API</td>
                        <td>Bạn đã mở - 14 thg 6, 2024</td>
                        <td>caodinhvu@gmail.com</td>
                    </tr>
                    <tr>
                        <td>Thiết kế ứng dụng chứng khoán với ASP .net core API</td>
                        <td>Bạn đã mở - 14 thg 6, 2024</td>
                        <td>caodinhvu@gmail.com</td>
                    </tr>
                </table>
            </div>

        </div>
    )
}


