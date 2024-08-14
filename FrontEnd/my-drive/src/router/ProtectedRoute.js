import React, {useEffect} from 'react';
import { Outlet, useNavigate } from 'react-router-dom';

export default function ProtectedRoute() {

    const navigate = useNavigate();

    useEffect(() => {
        //const isLoggedIn = localStorage.getItem('accessToken');
        if (!localStorage.getItem('accessToken')) {
            navigate("/login");
            return;
        }
    }, []);

    // if (!localStorage.getItem('accessToken')) {
    //     console.log("aaaaa")
    //     navigate("/login");
    //     return;
    // }

    return (
        <Outlet />
    )
}
