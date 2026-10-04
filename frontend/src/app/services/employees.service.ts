import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Employee } from '../models/leave-request.model';

@Injectable({ providedIn: 'root' })
export class EmployeesService {
  private readonly apiUrl = 'http://localhost:5080/api/employees';

  constructor(private readonly http: HttpClient) {}

  getAll(): Observable<Employee[]> {
    return this.http.get<Employee[]>(this.apiUrl);
  }
}
