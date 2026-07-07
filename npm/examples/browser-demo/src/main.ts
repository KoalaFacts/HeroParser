import { createVaporApp } from 'vue'
import App from './App.vue'
import './style.css'

// Mount our Vue Vapor application (bypassing Virtual DOM entirely)
createVaporApp(App as any).mount('#app')
