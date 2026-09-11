using System.Collections.Generic;

namespace IIChatTools.Services.DTO
{
    /// <summary>
    /// Обёртка для постраничного результата.
    /// </summary>
    /// <typeparam name="T">Тип элементов</typeparam>
    public class PagedResult<T>
    {
        /// <summary>
        /// Элементы текущей страницы.
        /// </summary>
        public IReadOnlyList<T> Items { get; set; }

        /// <summary>
        /// Номер страницы (1-индексация).
        /// </summary>
        public int Page { get; set; }

        /// <summary>
        /// Размер страницы.
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Общее количество записей.
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Общее количество страниц.
        /// </summary>
        public int TotalPages { get; set; }
    }
}