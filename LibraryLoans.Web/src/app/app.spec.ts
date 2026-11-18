import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { appConfig } from './app.config';

describe('App shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [...appConfig.providers, provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('should create the application shell and render the operational navigation', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const request = TestBed.inject(HttpTestingController).expectOne('/api/health/ready');
    request.flush('Healthy');
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('nav[aria-label="Navegação principal"]')).toBeTruthy();
    expect(compiled.textContent).toContain('Empréstimos');
    expect(compiled.textContent).toContain('Catálogo');
    TestBed.inject(HttpTestingController).verify();
  });
});
