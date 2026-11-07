
import { renderOverview } from './js/pages/overview.js';
const c = { innerHTML: '', querySelectorAll: () => [] };
renderOverview(c, {});
console.log('HTML length:', c.innerHTML.length);
console.log('Includes tbody rows:', c.innerHTML.includes('data-user-id'));
