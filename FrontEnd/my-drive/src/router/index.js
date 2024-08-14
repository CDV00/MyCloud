import { Outlet, createBrowserRouter } from "react-router-dom";
import Login from "../pages/login/Login";
import Home from "../pages/home/Home";
import {AuthProvider} from "../context/AuthProvider";
import ProtectedRoute from "./ProtectedRoute";
import ErrorPage from "../pages/ErrorPage";
import Recomment from "../components/recomment/recomment";
import MyDrive from "../components/myDrive/myDrive";
import MyDesktop from "../components/myDesktop/MyDesktop";
//import NoteList from "../component/NoteList";
//import Note from "../component/Note";
//import {LoadDataFolders} from '../utils/folderUtils';
//import {LoadDataNoteByFolderId, LoadDataNoteById} from '../utils/noteUtils';
const AuthLayout = () => {
    return <AuthProvider><Outlet /></AuthProvider>
}

export default createBrowserRouter([
    {
        element: <AuthLayout />,
        errorElement: <ErrorPage />,
        children: [
            {
                // element: <Home />,
                // path: '/'
                element: <ProtectedRoute />,
                children: [
                    {
                        element: <Home />,
                        path: '/',
                        children:[
                            {
                                element:<Recomment/>,
                                path:"/"
                            },
                            {
                                element:<MyDrive/>,
                                path:"/my-drive"
                            },
                            {
                                element:<MyDesktop/>,
                                path:"/my-desktop"
                            },

                        ]
                        // loader: LoadDataFolders,
                        // children: [
                        //     {
                        //         element: <NoteList />,
                        //         path: `folders/:folderId`,
                        //         loader: LoadDataNoteByFolderId,
                        //         children: [
                        //             {
                        //                 element: <Note />,
                        //                 path: 'note/:noteId',
                        //                 loader: LoadDataNoteById,

                        //             }
                        //         ]
                        //     }
                        // ]
                    }

                ]
            },
            {
                element: <Login />,
                path: '/login'
            }
        ]
    }

])