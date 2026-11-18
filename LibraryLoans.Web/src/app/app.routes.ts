import { Routes } from '@angular/router';

export const routes: Routes = [
	{
		path: '',
		loadComponent: () => import('./pages/dashboard/dashboard').then((module) => module.DashboardPage),
		title: 'Visão geral | Acervo',
	},
	{
		path: 'loans',
		loadComponent: () => import('./pages/loans/loans').then((module) => module.LoansPage),
		title: 'Empréstimos | Acervo',
	},
	{
		path: 'books',
		loadComponent: () => import('./pages/books/books').then((module) => module.BooksPage),
		title: 'Catálogo | Acervo',
	},
	{
		path: 'readers',
		loadComponent: () => import('./pages/readers/readers').then((module) => module.ReadersPage),
		title: 'Leitores | Acervo',
	},
	{
		path: 'audit',
		loadComponent: () => import('./pages/audit/audit').then((module) => module.AuditPage),
		title: 'Auditoria | Acervo',
	},
	{ path: '**', redirectTo: '' },
];
