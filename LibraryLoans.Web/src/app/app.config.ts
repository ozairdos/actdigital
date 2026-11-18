import { registerLocaleData } from '@angular/common';
import localePtBr from '@angular/common/locales/pt';
import { provideHttpClient } from '@angular/common/http';
import { ApplicationConfig, LOCALE_ID, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideIcons } from '@ng-icons/core';
import {
  lucideActivity,
  lucideArrowLeft,
  lucideArrowUpRight,
  lucideBookOpen,
  lucideCalendarDays,
  lucideCheck,
  lucideChevronDown,
  lucideChevronRight,
  lucideCircleAlert,
  lucideCircleCheck,
  lucideClock3,
  lucideFilter,
  lucideHistory,
  lucideHome,
  lucideLibrary,
  lucideMenu,
  lucideMoreHorizontal,
  lucidePlus,
  lucideRefreshCw,
  lucideRotateCcw,
  lucideSearch,
  lucideUsers,
  lucideX,
} from '@ng-icons/lucide';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';

registerLocaleData(localePtBr);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: LOCALE_ID, useValue: 'pt-BR' },
    provideHttpClient(),
    provideRouter(routes),
    provideIcons({
      lucideActivity,
      lucideArrowLeft,
      lucideArrowUpRight,
      lucideBookOpen,
      lucideCalendarDays,
      lucideCheck,
      lucideChevronDown,
      lucideChevronRight,
      lucideCircleAlert,
      lucideCircleCheck,
      lucideClock3,
      lucideFilter,
      lucideHistory,
      lucideHome,
      lucideLibrary,
      lucideMenu,
      lucideMoreHorizontal,
      lucidePlus,
      lucideRefreshCw,
      lucideRotateCcw,
      lucideSearch,
      lucideUsers,
      lucideX,
    }),
  ]
};
