import { createApp } from 'vue';
import AppRoot from './AppRoot.vue';
import './index.css';
import { createWebRouter } from './router/Routes';

createApp(AppRoot).use(createWebRouter()).mount('#root');
