import React from 'react';
import './home.scss';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { NavLink, Outlet } from 'react-router-dom';

export default function home() {
  return (

    <>
      <div id="home">
        <div id='header'>header</div>
        <div id='main-content'>
          <div id='nav'>
            <button>Mới</button>
            <div id='nav-main'>
              <ul>
                <li><NavLink to='/'> <FontAwesomeIcon icon={['fab', 'google']} /> Trang chủ</NavLink></li>
                <li> <NavLink to="/my-drive">Drive của tôi </NavLink> </li>
                <li><NavLink to="/my-desktop">Máy tính</NavLink></li>
                <li><NavLink to="/my-desktop">Được chia sẻ với tôi</NavLink></li>
                <li><NavLink to="/my-desktop">Gần đây</NavLink></li>
                <li><NavLink to="/my-desktop">Có gắn dấu sao</NavLink></li>
                <li><NavLink to="/my-desktop">Nội dung rác</NavLink></li>
                <li><NavLink to="/my-desktop">Thùng rác</NavLink></li>
                <li><NavLink to="/my-desktop">Bộ nhớ</NavLink></li>
              </ul>
            </div>
          </div>
          <div id='content'>
            <Outlet />
          </div>
          <div id='detail'>
            <div>
              <h3>Bản tin tuyển dụng tháng 04_2024.docx.pdf</h3>

            </div>
          </div>
        </div>
        <div id='footer'>
          footer
        </div>

      </div>
    </>

  )
}
