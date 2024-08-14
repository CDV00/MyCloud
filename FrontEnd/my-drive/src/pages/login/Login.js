import React, { useState, useEffect } from 'react';
import './login.scss';
import {SetAccessTokenLocalStorege} from "../../context/AuthProvider";
import {useNavigate } from 'react-router-dom';




export default function Login() {

  const navigate = useNavigate();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");

  const login = ()=>{
    SetAccessTokenLocalStorege("token");
    navigate("/");
  }

  useEffect(() => {
    //const isLoggedIn = localStorage.getItem('accessToken');
    if (localStorage.getItem('accessToken')) {
        navigate("/");
        return;
    }
  }, []);

  const onChangeUsername = (event)=>{
    setUsername(event.target.value)
  }
  const onChangePassword = (event)=>{
    setPassword(event.target.value)
  }

  return (
    <div className="login">
      <h1>Login</h1>
      <div>
        <input type="text" name="u" placeholder="Username" required="required" value={username} onChange={(event)=>onChangeUsername(event)} />
        <input type="password" name="p" placeholder="Password" required="required" value={password} onChange={(event)=>onChangePassword(event)} />
        <button onClick={login} className="btn btn-primary btn-block btn-large">Login</button>
        
      </div>
    </div>
  )
}
