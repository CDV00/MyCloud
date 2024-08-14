
import './App.css';
import { RouterProvider } from 'react-router-dom';
//import { Container } from '@mui/material';

import React from 'react';
import ReactDOM from 'react-dom/client';
//import '../node_modules/react-draft-wysiwyg/dist/react-draft-wysiwyg.css';

//import { RouterProvider } from 'react-router-dom';
import router from './router/index';
import "./fontawesome";
//import './firebase/config';


function App() {
  return (
    //<Container maxWidth='lg' sx={{textAlign: 'center', marginTop:'50px'}}>

      <RouterProvider router={router} />
    //</Container>
  );
}

export default App;
