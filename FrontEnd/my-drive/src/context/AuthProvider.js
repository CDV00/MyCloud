import React, { createContext, useEffect, useState } from 'react';
//import {getAuth} from 'firebase/auth';
import {useNavigate}  from 'react-router-dom';

export const AuthContext = createContext();

const AuthProvider = ({ children }) => {

    const [user, setUser] = useState({});
    const navigate = useNavigate();

    //const auth = getAuth();

    // useEffect(()=>{
    //     const unsubcribed = auth.onIdTokenChanged((user)=>{
    //         console.log('[From AuthProvider]', {user});
    //         if(user?.uid){
    //             setUser(user);
    //             localStorage.setItem('accessToken', user.accessToken);
    //             return;
    //         }

    //         //
    //         setUser({});
    //         localStorage.clear();
    //         navigate('/login');
    //     })
    //     return () => {
    //         unsubcribed();
    //     }
    // },[auth])

    

    return (
        <AuthContext.Provider value={{user, setUser}}>
            {children}
        </AuthContext.Provider>
    )
}
const SetAccessTokenLocalStorege = (accessToken)=>{
    localStorage.setItem('accessToken', accessToken);
}
export {AuthProvider, SetAccessTokenLocalStorege}